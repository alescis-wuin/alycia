using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;

namespace Alycia.Modules.Identity.Infrastructure;

// SQL-level single-row locking serializes HTTP attempts and admin regeneration
// across processes and restarts. No business-domain dependency on PostgreSQL.
public static class OwnerBootstrapStore
{
    private const long AdvisoryLockKey = 0x414C594349413037; // "ALYCIA07"
    public const int MaxIncorrectAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static async Task LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key)", connection, transaction);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<bool> OwnerExistsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM identity.users)", connection, transaction);
        return (bool)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("Owner state is unavailable."));
    }

    public static async Task<BootstrapState?> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT secret_hash, expires_at_utc, incorrect_attempts, consumed_at_utc " +
            "FROM identity.owner_bootstrap WHERE id = 1 FOR UPDATE",
            connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var state = new BootstrapState(
            reader.GetFieldValue<byte[]>(0),
            reader.GetFieldValue<DateTime>(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTime>(3));
        return state;
    }

    public static async Task ReplaceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        byte[] hash, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO identity.owner_bootstrap
                (id, secret_hash, expires_at_utc, incorrect_attempts, consumed_at_utc)
            VALUES (1, @hash, @expires, 0, NULL)
            ON CONFLICT (id) DO UPDATE SET
                secret_hash = EXCLUDED.secret_hash,
                expires_at_utc = EXCLUDED.expires_at_utc,
                incorrect_attempts = 0,
                consumed_at_utc = NULL
            """, connection, transaction);
        command.Parameters.Add("hash", NpgsqlDbType.Bytea).Value = hash;
        command.Parameters.Add("expires", NpgsqlDbType.TimestampTz).Value = expiresAtUtc.UtcDateTime;
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task IncrementIncorrectAttemptsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE identity.owner_bootstrap SET incorrect_attempts = incorrect_attempts + 1
            WHERE id = 1 AND incorrect_attempts < 5 AND consumed_at_utc IS NULL
            """, connection, transaction);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidOperationException("Bootstrap counter could not be updated.");
        }
    }

    public static async Task ConsumeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE identity.owner_bootstrap SET consumed_at_utc = @consumed
            WHERE id = 1 AND consumed_at_utc IS NULL
            """, connection, transaction);
        command.Parameters.Add("consumed", NpgsqlDbType.TimestampTz).Value = now.UtcDateTime;
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidOperationException("Bootstrap consumption could not be committed.");
        }
    }

    public static byte[] HashSecret(string secret) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));

    public static bool Matches(string supplied, byte[] storedHash)
    {
        // Compare fixed-size hashes; malformed but nonempty secrets still count.
        var candidate = HashSecret(supplied);
        return storedHash.Length == candidate.Length
            && CryptographicOperations.FixedTimeEquals(storedHash, candidate);
    }
}

public sealed record BootstrapState(
    byte[] SecretHash, DateTime ExpiresAtUtc, int IncorrectAttempts,
    DateTime? ConsumedAtUtc);
