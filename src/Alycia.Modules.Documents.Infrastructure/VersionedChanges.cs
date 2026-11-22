using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Documents.Infrastructure;

internal static class VersionedChanges
{
    internal static async Task SaveVersionedAsync(this DbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ChatConflictException();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.SerializationFailure })
        {
            throw new ChatConflictException();
        }
    }
}
