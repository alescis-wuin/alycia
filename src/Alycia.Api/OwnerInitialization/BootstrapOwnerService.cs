using System.Text;
using Alycia.Modules.Identity.Application;
using Alycia.Modules.Identity.Domain;
using Alycia.Modules.Identity.Infrastructure;
using Npgsql;

namespace Alycia.Api.OwnerInitialization;

public enum BootstrapResultCode
{
    Created,
    AlreadyInitialized,
    NotIssued,
    Expired,
    Locked,
    InvalidSecret,
    InvalidLogin,
    InvalidPassword,
    PasswordConfirmationMismatch
}

public sealed record BootstrapResult(BootstrapResultCode Code, Guid? OwnerId = null);

public sealed class BootstrapOwnerService(
    string connectionString,
    OwnerInitializationPersistence persistence,
    OwnerPasswordPolicy passwordPolicy,
    IOwnerPasswordHasher passwordHasher,
    TimeProvider timeProvider)
{
    public async Task<bool> IsInitializedAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // A read-only status check does not need to acquire the bootstrap lock.
        return await OwnerBootstrapStore.OwnerExistsAsync(connection, transaction, ct);
    }

    public async Task<BootstrapResult> InitializeAsync(
        string? secret, string? loginIdentifier, string? password,
        string? confirmation, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await OwnerBootstrapStore.LockAsync(connection, transaction, ct);

        if (await OwnerBootstrapStore.OwnerExistsAsync(connection, transaction, ct))
        {
            return new BootstrapResult(BootstrapResultCode.AlreadyInitialized);
        }

        var state = await OwnerBootstrapStore.ReadAsync(connection, transaction, ct);
        if (state is null) return new BootstrapResult(BootstrapResultCode.NotIssued);
        var now = timeProvider.GetUtcNow();
        if (state.ConsumedAtUtc is not null)
            return new BootstrapResult(BootstrapResultCode.AlreadyInitialized);
        if (state.ExpiresAtUtc <= now.UtcDateTime)
            return new BootstrapResult(BootstrapResultCode.Expired);
        if (state.IncorrectAttempts >= OwnerBootstrapStore.MaxIncorrectAttempts)
            return new BootstrapResult(BootstrapResultCode.Locked);

        if (string.IsNullOrEmpty(secret))
            return new BootstrapResult(BootstrapResultCode.InvalidSecret);

        if (!OwnerBootstrapStore.Matches(secret, state.SecretHash))
        {
            await OwnerBootstrapStore.IncrementIncorrectAttemptsAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
            return new BootstrapResult(state.IncorrectAttempts + 1 >= OwnerBootstrapStore.MaxIncorrectAttempts
                ? BootstrapResultCode.Locked : BootstrapResultCode.InvalidSecret);
        }

        User owner;
        try
        {
            owner = User.Create(loginIdentifier);
        }
        catch (ArgumentException)
        {
            return new BootstrapResult(BootstrapResultCode.InvalidLogin);
        }

        string normalizedPassword;
        try
        {
            normalizedPassword = passwordPolicy.NormalizeAndValidate(password);
        }
        catch (ArgumentException)
        {
            return new BootstrapResult(BootstrapResultCode.InvalidPassword);
        }

        // Normalize confirmation using precisely the same NFC policy, but do
        // not run the password blocklist twice or charge a secret attempt.
        if (confirmation is null)
        {
            return new BootstrapResult(BootstrapResultCode.PasswordConfirmationMismatch);
        }
        string normalizedConfirmation;
        try
        {
            normalizedConfirmation = confirmation.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return new BootstrapResult(BootstrapResultCode.PasswordConfirmationMismatch);
        }
        if (!string.Equals(normalizedPassword, normalizedConfirmation, StringComparison.Ordinal))
        {
            return new BootstrapResult(BootstrapResultCode.PasswordConfirmationMismatch);
        }

        owner.SetPasswordHash(passwordHasher.HashPassword(owner, normalizedPassword));
        await persistence.InitializeInTransactionAsync(owner, connection, transaction, ct);
        await OwnerBootstrapStore.ConsumeAsync(connection, transaction, now, ct);
        await transaction.CommitAsync(ct);
        return new BootstrapResult(BootstrapResultCode.Created, owner.Id);
    }

}
