using Alycia.Api.OwnerInitialization;
using Alycia.Modules.Identity.Application;
using Alycia.Modules.Identity.Domain;
using Alycia.Modules.Identity.Infrastructure;
using Alycia.Modules.Workspaces.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.OwnerInitialization.IntegrationTests;

[Collection("S7 database")]
public sealed class OwnerInitializationPostgresTests
{
    [Fact]
    public async Task Initialization_RollsBack_RetriesAndResistsConcurrentSecondOwner()
    {
        var connectionString = Environment.GetEnvironmentVariable("ALYCIA_S7_TEST_CONNECTION")
            ?? throw new InvalidOperationException(
                "ALYCIA_S7_TEST_CONNECTION must target a dedicated migrated test database.");
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (databaseName is null || !System.Text.RegularExpressions.Regex.IsMatch(
            databaseName, @"^alycia_s7_test_[a-z0-9_]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Only isolated S7 test databases are permitted.");
        }

        var persistence = new OwnerInitializationPersistence(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal(0L, await CountAsync(connection, "SELECT count(*) FROM identity.users"));
        Assert.Equal(2L, await CountAsync(connection, "SELECT count(*) FROM workspaces.workspaces"));
        Assert.Equal(2L, await CountAsync(connection,
            "SELECT count(*) FROM workspaces.workspaces WHERE owner_user_id IS NULL"));

        var ownerA = CreateHashedOwner("owner_a");
        await ExecuteAsync(connection, """
            CREATE FUNCTION workspaces.s7_test_fail_owner_assignment() RETURNS trigger
            LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 's7 test rollback injection'; END $$;
            """);
        await ExecuteAsync(connection, """
            CREATE TRIGGER s7_test_fail_owner_assignment
            BEFORE UPDATE OF owner_user_id ON workspaces.workspaces
            FOR EACH ROW EXECUTE FUNCTION workspaces.s7_test_fail_owner_assignment();
            """);

        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(
                () => persistence.InitializeAsync(ownerA));
            Assert.Equal("P0001", error.SqlState);
        }
        finally
        {
            await ExecuteAsync(connection,
                "DROP TRIGGER s7_test_fail_owner_assignment ON workspaces.workspaces");
            await ExecuteAsync(connection,
                "DROP FUNCTION workspaces.s7_test_fail_owner_assignment()");
        }

        // The failed transaction must leave both modules unchanged.
        Assert.Equal(0L, await CountAsync(connection, "SELECT count(*) FROM identity.users"));
        Assert.Equal(2L, await CountAsync(connection,
            "SELECT count(*) FROM workspaces.workspaces WHERE owner_user_id IS NULL"));

        var ownerB = CreateHashedOwner("owner_b");
        var candidates = new[] { ownerA, ownerB };
        var outcomes = await Task.WhenAll(candidates.Select(async owner =>
        {
            try
            {
                return await persistence.InitializeAsync(owner);
            }
            catch (OwnerAlreadyInitializedException)
            {
                return Guid.Empty;
            }
        }));

        Assert.Equal(1, outcomes.Count(id => id != Guid.Empty));
        var committedId = Assert.Single(outcomes, id => id != Guid.Empty);

        // Query again through a fresh connection to prove committed persistence.
        await using var restartedConnection = new NpgsqlConnection(connectionString);
        await restartedConnection.OpenAsync();
        Assert.Equal(1L, await CountAsync(restartedConnection,
            "SELECT count(*) FROM identity.users"));
        Assert.Equal(0L, await CountAsync(restartedConnection,
            "SELECT count(*) FROM workspaces.workspaces WHERE owner_user_id IS NULL"));
        Assert.Equal(2L, await CountAsync(restartedConnection,
            "SELECT count(*) FROM workspaces.workspaces WHERE owner_user_id = @owner", committedId));

        await Assert.ThrowsAsync<OwnerAlreadyInitializedException>(() =>
            new OwnerInitializationPersistence(connectionString)
                .InitializeAsync(CreateHashedOwner("another_owner")));
    }

    private static User CreateHashedOwner(string login)
    {
        var owner = User.Create(login);
        owner.SetPasswordHash(new OwnerPasswordHasher().HashPassword(
            owner, "A lengthy test-only passphrase"));
        return owner;
    }

    private static async Task<long> CountAsync(
        NpgsqlConnection connection, string sql, Guid? ownerId = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (ownerId is { } id)
        {
            command.Parameters.AddWithValue("owner", id);
        }

        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Count query returned no result."));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
