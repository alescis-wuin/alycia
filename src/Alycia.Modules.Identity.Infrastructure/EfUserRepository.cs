using Alycia.Modules.Identity.Application;
using Alycia.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Identity.Infrastructure;

public sealed class EfUserRepository(IdentityDbContext dbContext) : IUserRepository
{
    public Task<bool> OwnerExistsAsync(CancellationToken cancellationToken = default) =>
        dbContext.Users.AsNoTracking().AnyAsync(cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException error &&
            error.SqlState == PostgresErrorCodes.UniqueViolation &&
            error.ConstraintName == "ux_identity_users_single_owner")
        {
            dbContext.Entry(user).State = EntityState.Detached;
            throw new OwnerAlreadyInitializedException();
        }
    }
}
