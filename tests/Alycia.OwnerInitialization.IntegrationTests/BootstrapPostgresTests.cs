using System.Net;
using Alycia.Api.OwnerInitialization;
using Alycia.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Alycia.OwnerInitialization.IntegrationTests;

[Collection("S7 database")]
public sealed class BootstrapPostgresTests
{
    private const string Password = "A long private test passphrase";

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("172.24.0.3", false)]
    [InlineData("192.168.1.3", false)]
    [InlineData("10.10.0.2", false)]
    public void BootstrapRejectsNonLoopbackPeers(string ip, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, BootstrapEndpoints.IsLocalConnection(context));
    }

    [Fact]
    public void BootstrapRejectsPeerWithMissingAddress()
    {
        Assert.False(BootstrapEndpoints.IsLocalConnection(new DefaultHttpContext()));
    }

    [Fact]
    public async Task Bootstrap_IsDurableRateLimitedReplaceableAndAtomic()
    {
        var connectionString = Environment.GetEnvironmentVariable("ALYCIA_S7_TEST_CONNECTION")
            ?? throw new InvalidOperationException("A dedicated test database is required.");
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (databaseName is null || !System.Text.RegularExpressions.Regex.IsMatch(
            databaseName, @"^alycia_s7_test_[a-z0-9_]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Only isolated S7 test databases are permitted.");
        }

        var issuer = new OwnerBootstrapIssuer(connectionString, TimeProvider.System);
        var service = NewService(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Test script creates a private temporary database. Test execution is
        // serialized with the existing owner transaction test.
        await ResetAsync(connection);
        try
        {
            var expiredSecret = await issuer.GenerateAsync();
            Assert.Equal(64, expiredSecret.Length);
            Assert.All(expiredSecret, character => Assert.Contains(character, "0123456789ABCDEF"));
            await Assert.ThrowsAsync<ActiveBootstrapSecretException>(() => issuer.GenerateAsync());

            // Deliberately expire only this isolated test database's verifier.
            await ExecuteAsync(connection, """
                UPDATE identity.owner_bootstrap
                SET expires_at_utc = NOW() - INTERVAL '1 second' WHERE id = 1
                """);
            Assert.Equal(BootstrapResultCode.Expired,
                (await service.InitializeAsync(expiredSecret, "alice", Password, Password)).Code);
            var originalSecret = await issuer.GenerateAsync();
            Assert.NotEqual(expiredSecret, originalSecret);
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT incorrect_attempts FROM identity.owner_bootstrap WHERE id=1"));

            Assert.Equal(BootstrapResultCode.InvalidSecret,
                (await service.InitializeAsync(null, "alice", Password, Password)).Code);
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT incorrect_attempts FROM identity.owner_bootstrap WHERE id=1"));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(BootstrapResultCode.InvalidSecret,
                    (await service.InitializeAsync($"incorrect-{n}", "alice", Password, Password)).Code);
            }
            Assert.Equal(BootstrapResultCode.InvalidLogin,
                (await service.InitializeAsync(originalSecret, "!", Password, Password)).Code);
            Assert.Equal(BootstrapResultCode.InvalidPassword,
                (await service.InitializeAsync(originalSecret, "alice", "short", "short")).Code);
            Assert.Equal(BootstrapResultCode.PasswordConfirmationMismatch,
                (await service.InitializeAsync(originalSecret, "alice", Password, "not the same")).Code);
            Assert.Equal(3L, await ScalarAsync(connection,
                "SELECT incorrect_attempts FROM identity.owner_bootstrap WHERE id=1"));

            // A new service instance simulates a process restart; the counter persists.
            var restarted = NewService(connectionString);
            Assert.Equal(BootstrapResultCode.InvalidSecret,
                (await restarted.InitializeAsync("wrong 4", "alice", Password, Password)).Code);
            Assert.Equal(BootstrapResultCode.Locked,
                (await restarted.InitializeAsync("wrong 5", "alice", Password, Password)).Code);
            Assert.Equal(5L, await ScalarAsync(connection,
                "SELECT incorrect_attempts FROM identity.owner_bootstrap WHERE id=1"));
            Assert.Equal(BootstrapResultCode.Locked,
                (await restarted.InitializeAsync(originalSecret, "alice", Password, Password)).Code);

            var regenerated = await issuer.GenerateAsync();
            Assert.NotEqual(originalSecret, regenerated);
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT incorrect_attempts FROM identity.owner_bootstrap WHERE id=1"));
            Assert.Equal(BootstrapResultCode.InvalidSecret,
                (await restarted.InitializeAsync(originalSecret, "alice", Password, Password)).Code);

            // Inject a database failure inside the shared owner + Workspace +
            // secret-consumption transaction; no partial owner may survive.
            await ExecuteAsync(connection, """
                CREATE FUNCTION workspaces.s7_bootstrap_fail() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 's7 injected failure'; END $$;
                """);
            await ExecuteAsync(connection, """
                CREATE TRIGGER s7_bootstrap_fail BEFORE UPDATE OF owner_user_id
                ON workspaces.workspaces FOR EACH ROW
                EXECUTE FUNCTION workspaces.s7_bootstrap_fail();
                """);
            try
            {
                await Assert.ThrowsAsync<PostgresException>(() =>
                    restarted.InitializeAsync(regenerated, "alice", Password, Password));
            }
            finally
            {
                await ExecuteAsync(connection,
                    "DROP TRIGGER s7_bootstrap_fail ON workspaces.workspaces");
                await ExecuteAsync(connection,
                    "DROP FUNCTION workspaces.s7_bootstrap_fail()");
            }
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM identity.users"));
            Assert.Equal(2L, await ScalarAsync(connection,
                "SELECT count(*) FROM workspaces.workspaces WHERE owner_user_id IS NULL"));
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT incorrect_attempts FROM identity.owner_bootstrap WHERE id=1"));
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT count(*) FROM identity.owner_bootstrap WHERE consumed_at_utc IS NOT NULL"));

            var outcomes = await Task.WhenAll(
                restarted.InitializeAsync(regenerated, "alice", Password, Password),
                NewService(connectionString).InitializeAsync(regenerated, "bob", Password, Password));
            var successful = Assert.Single(outcomes, outcome => outcome.Code == BootstrapResultCode.Created);
            Assert.NotNull(successful.OwnerId);
            Assert.Equal(BootstrapResultCode.AlreadyInitialized,
                Assert.Single(outcomes, outcome => outcome.Code != BootstrapResultCode.Created).Code);

            await using var freshConnection = new NpgsqlConnection(connectionString);
            await freshConnection.OpenAsync();
            Assert.Equal(1L, await ScalarAsync(freshConnection, "SELECT count(*) FROM identity.users"));
            Assert.Equal(2L, await ScalarAsync(freshConnection,
                "SELECT count(*) FROM workspaces.workspaces WHERE owner_user_id = @owner",
                successful.OwnerId));
            Assert.Equal(1L, await ScalarAsync(freshConnection,
                "SELECT count(*) FROM identity.owner_bootstrap WHERE consumed_at_utc IS NOT NULL"));
            Assert.True(await NewService(connectionString).IsInitializedAsync());
            Assert.Equal(BootstrapResultCode.AlreadyInitialized,
                (await restarted.InitializeAsync(regenerated, "charlie", Password, Password)).Code);
            await Assert.ThrowsAsync<BootstrapAlreadyInitializedException>(() => issuer.GenerateAsync());
        }
        finally
        {
            await ResetAsync(connection);
        }
    }

    private static BootstrapOwnerService NewService(string connectionString) => new(
        connectionString,
        new OwnerInitializationPersistence(connectionString),
        LocalBlockedOwnerPasswords.CreatePolicy(),
        new OwnerPasswordHasher(),
        TimeProvider.System);

    private static async Task ResetAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, "UPDATE workspaces.workspaces SET owner_user_id = NULL");
        await ExecuteAsync(connection, "DELETE FROM identity.users");
        await ExecuteAsync(connection, "DELETE FROM identity.owner_bootstrap");
    }

    private static async Task<long> ScalarAsync(
        NpgsqlConnection connection, string sql, Guid? id = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is not null) command.Parameters.AddWithValue("owner", id.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Missing scalar result."));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
