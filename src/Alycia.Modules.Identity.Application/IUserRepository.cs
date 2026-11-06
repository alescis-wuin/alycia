using Alycia.Modules.Identity.Domain;

namespace Alycia.Modules.Identity.Application;

public interface IUserRepository
{
    Task<bool> OwnerExistsAsync(CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);
}
