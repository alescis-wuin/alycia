using Alycia.Modules.Identity.Application;
using Alycia.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Alycia.Modules.Identity.Infrastructure;

public sealed class OwnerPasswordHasher : IOwnerPasswordHasher
{
    // IdentityV3 includes algorithm, iteration count and salt in the verifier.
    // Future password checks can return SuccessRehashNeeded after cost upgrades.
    private readonly IPasswordHasher<User> _hasher = new PasswordHasher<User>(
        Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 310_000
        }));

    public string HashPassword(User user, string normalizedPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(normalizedPassword);
        return _hasher.HashPassword(user, normalizedPassword);
    }
}
