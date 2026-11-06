using Alycia.Modules.Identity.Domain;

namespace Alycia.Modules.Identity.Application;

public interface IOwnerPasswordHasher
{
    string HashPassword(User user, string normalizedPassword);
}
