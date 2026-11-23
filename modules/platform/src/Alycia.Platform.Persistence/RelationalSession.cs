using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Alycia.Platform.Persistence;

public sealed class RelationalSession
{
    private readonly DatabaseFacade? database;
    private readonly DbTransaction? enlisted;

    public RelationalSession(DatabaseFacade database) => this.database = database;

    // The initiator of an external transaction retains the single right to commit it.
    public RelationalSession(DbTransaction transaction) =>
        enlisted = transaction.Connection is not null ? transaction : throw new ArgumentException("Transaction terminée.", nameof(transaction));

    public async Task<T> AtomicAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        if (database is null) throw new InvalidOperationException("La transaction appartient à l’appelant.");
        await using var transaction = await database.BeginTransactionAsync(ct);
        var result = await operation(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<T> WithConnectionAsync<T>(Func<DbConnection, DbTransaction?, CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        if (enlisted is not null)
            return await operation(enlisted.Connection ?? throw new InvalidOperationException("Transaction terminée."), enlisted, ct);
        var facade = database ?? throw new InvalidOperationException("Session sans connexion.");
        await facade.OpenConnectionAsync(ct);
        try { return await operation(facade.GetDbConnection(), facade.CurrentTransaction?.GetDbTransaction(), ct); }
        finally { await facade.CloseConnectionAsync(); }
    }

    public static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
