using System.Security.Cryptography;
using Npgsql;

namespace Alycia.Modules.Identity.Infrastructure;

public sealed class OwnerBootstrapIssuer(string connectionString, TimeProvider timeProvider)
{
    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await OwnerBootstrapStore.LockAsync(connection, transaction, cancellationToken);

        if (await OwnerBootstrapStore.OwnerExistsAsync(connection, transaction, cancellationToken))
        {
            throw new BootstrapAlreadyInitializedException();
        }

        var existing = await OwnerBootstrapStore.ReadAsync(connection, transaction, cancellationToken);
        if (existing is { ConsumedAtUtc: null, IncorrectAttempts: < OwnerBootstrapStore.MaxIncorrectAttempts }
            && existing.ExpiresAtUtc > now.UtcDateTime)
        {
            throw new ActiveBootstrapSecretException();
        }

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await OwnerBootstrapStore.ReplaceAsync(connection, transaction,
            OwnerBootstrapStore.HashSecret(secret),
            now.Add(OwnerBootstrapStore.Lifetime), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return secret; // The caller may display it once; never log or persist this value.
    }
}

public sealed class ActiveBootstrapSecretException()
    : InvalidOperationException("An active bootstrap secret already exists.");

public sealed class BootstrapAlreadyInitializedException()
    : InvalidOperationException("The owner is already initialized.");
