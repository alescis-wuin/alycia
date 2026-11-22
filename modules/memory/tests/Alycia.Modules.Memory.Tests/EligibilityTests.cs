using Alycia.Modules.Testing;
using Npgsql;

namespace Alycia.Modules.Memory.Tests;

public sealed class EligibilityTests
{
    private sealed class DatabaseFact : FactAttribute
    {
        public DatabaseFact()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")))
                Skip = "Serveur PostgreSQL de test requis.";
        }
    }

    [DatabaseFact]
    public async Task Eligibility_obeys_owner_sequence_date_and_cancellation_in_both_adapters()
    {
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        var name = "alycia_memory_contract_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
            await using var db = new NpgsqlConnection(isolated);
            await db.OpenAsync();
            await using (var create = new NpgsqlCommand("""
                CREATE SCHEMA chat;
                CREATE TABLE chat.temporal_memory(owner uuid, sequence integer, observed timestamptz, facts jsonb);
                """, db)) await create.ExecuteNonQueryAsync();
            var owner = Guid.NewGuid();
            var outsider = Guid.NewGuid();
            var cutoff = DateTimeOffset.UnixEpoch.AddDays(2);
            RecordedMemorySource[] rows = [new(owner, 1, cutoff.AddDays(-1), "one"), new(owner, 2, cutoff, "late"),
                new(owner, 3, cutoff.AddHours(-1), "beyond"), new(outsider, 1, cutoff.AddDays(-1), "outsider")];
            foreach (var row in rows)
            {
                await using var write = new NpgsqlCommand("""
                    INSERT INTO chat.temporal_memory VALUES ($1,$2,$3,jsonb_build_array(jsonb_build_object('sha256',$4::text)))
                    """, db);
                write.Parameters.AddWithValue(row.Owner);
                write.Parameters.AddWithValue(row.Sequence);
                write.Parameters.AddWithValue(row.Observed);
                write.Parameters.AddWithValue(row.Sha256);
                await write.ExecuteNonQueryAsync();
            }
            foreach (var adapter in new IMemoryVectorEligibility[] {new MemoryVectorEligibility(isolated), new SnapshotMemoryEligibility(rows)})
            {
                var selected = new List<MemoryVectorSource>();
                await foreach (var page in adapter.PagesAsync(owner, 2, cutoff, default)) selected.AddRange(page);
                Assert.Equal(new MemoryVectorSource(1, "one"), Assert.Single(selected));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                {
                    await foreach (var _ in adapter.PagesAsync(owner, 2, cutoff, new(true))) { }
                });
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
