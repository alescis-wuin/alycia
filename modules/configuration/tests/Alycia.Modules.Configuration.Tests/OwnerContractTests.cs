using Alycia.Contracts.Chat;
using Alycia.Contracts.Documents;
using Alycia.Modules.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Alycia.Modules.Configuration;

namespace Alycia.Modules.Chat.Tests;

public sealed class OwnerContractTests
{
    [PostgresFact]
    public Task Import_limits_preserve_defaults_overrides_and_cancellation_in_both_adapters() =>
        WithDatabaseAsync(async db =>
        {
            var store = new ImportSettingsStore(db);
            foreach (var limits in new[] { new ImportLimits(), new ImportLimits(FileMiB: 2, BatchMiB: 4) })
            {
                if (limits != new ImportLimits()) await store.SaveAsync(limits, default);
                foreach (var reader in new IImportLimitsReader[] { store, new FixedImportLimits(limits) })
                {
                    Assert.Equal(limits, await reader.LoadAsync(default));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.LoadAsync(new CancellationToken(true)));
                }
            }
        });

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")))
                Skip = "Serveur PostgreSQL de test requis.";
        }
    }

    private static async Task WithDatabaseAsync(Func<DbContext, Task> verify)
    {
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        var database = "alycia_configuration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ConnectionString;
            await using var db = new DbContext(new DbContextOptionsBuilder().UseNpgsql(isolated).Options);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA chat;
                CREATE TABLE chat.import_settings (id integer PRIMARY KEY CHECK (id = 1), settings_json text NOT NULL);
                """);
            await verify(db);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
