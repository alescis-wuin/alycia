using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Alycia.Platform.Persistence;

// Scoped to one unit of work. Only infrastructure adapters see the enlisted connection.
public sealed class RelationalSession(DatabaseFacade database)
{
    public async Task<T> AtomicAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await using var transaction = await database.BeginTransactionAsync(ct);
        var result = await operation(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<T> WithConnectionAsync<T>(Func<DbConnection, DbTransaction?, CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        await database.OpenConnectionAsync(ct);
        try
        {
            return await operation(database.GetDbConnection(), database.CurrentTransaction?.GetDbTransaction(), ct);
        }
        finally { await database.CloseConnectionAsync(); }
    }
}
