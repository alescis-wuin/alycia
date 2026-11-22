using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")))
            Skip = "Serveur PostgreSQL de test requis.";
    }
}


internal static class MemoryTestDatabase
{
    internal static async Task WithDatabaseAsync(Func<DbContext, Task> verify)
    {
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        var database = "alycia_memory_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ConnectionString;
            await using var db = new DbContext(new DbContextOptionsBuilder().UseNpgsql(isolated).Options);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA chat;
                CREATE TABLE chat.conversations ("Id" uuid PRIMARY KEY);
                CREATE TABLE chat.temporal_memory (
                    owner uuid NOT NULL REFERENCES chat.conversations("Id") ON DELETE CASCADE,
                    sequence integer NOT NULL, observed timestamptz NOT NULL, topics text[] NOT NULL,
                    facts jsonb NOT NULL, search tsvector NOT NULL, version smallint NOT NULL,
                    PRIMARY KEY(owner,sequence));
                CREATE INDEX ix_temporal_topics ON chat.temporal_memory USING gin(topics);
                CREATE INDEX ix_temporal_search ON chat.temporal_memory USING gin(search);
                """);
            await verify(db);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    internal static Task WithSingleConnection(Func<DbContext, NpgsqlConnection, Guid, Task> verify) =>
        WithDatabaseAsync(async seed =>
        {
            var owner = Guid.NewGuid();
            await seed.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chat.conversations VALUES ({owner})");
            var connection = new NpgsqlConnectionStringBuilder(seed.Database.GetConnectionString())
            {
                Pooling = true, MaxPoolSize = 1, Timeout = 4, ApplicationName = "pending-input"
            }.ConnectionString;
            await using var gate = new NpgsqlConnection(connection);
            await gate.OpenAsync();
            await using var db = new DbContext(new DbContextOptionsBuilder().UseNpgsql(connection).Options);
            try { await verify(db, gate, owner); }
            finally { NpgsqlConnection.ClearPool(gate); }
        });
}
