using Alycia.Modules.Identity.Domain;
using Alycia.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Alycia.Modules.Identity.Tests;

public sealed class IdentityInfrastructureTests
{
    [Fact]
    public void HashPassword_CreatesVerifiableSaltedCredentialWithoutPlaintext()
    {
        var user = User.Create("owner");
        var policy = LocalBlockedOwnerPasswords.CreatePolicy();
        var password = policy.NormalizeAndValidate("a sufficiently long private phrase");
        var hasher = new OwnerPasswordHasher();

        var first = hasher.HashPassword(user, password);
        var second = hasher.HashPassword(user, password);
        user.SetPasswordHash(first);

        var verifier = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 310_000
        }));

        Assert.NotEqual(password, first);
        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerificationResult.Success,
            verifier.VerifyHashedPassword(user, user.PasswordHash, password));
        Assert.Equal(PasswordVerificationResult.Failed,
            verifier.VerifyHashedPassword(user, user.PasswordHash, "not the valid password"));
    }

    [Theory]
    [InlineData("password123456789")]
    [InlineData("PASSWORD123456789")]
    [InlineData("correct horse battery staple")]
    public void LocalList_RejectsKnownWeakFullValues(string password)
    {
        var policy = LocalBlockedOwnerPasswords.CreatePolicy();
        Assert.Throws<ArgumentException>(() => policy.NormalizeAndValidate(password));
    }

    [Fact]
    public void LocalList_PreservesSignificantSpaces()
    {
        var policy = LocalBlockedOwnerPasswords.CreatePolicy();
        Assert.Equal(" password123456789 ",
            policy.NormalizeAndValidate(" password123456789 "));
    }

    [Fact]
    public void DbContext_MapsUsersToSingleOwnerSlot()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=alycia;Username=alycia")
            .Options;
        using var dbContext = new IdentityDbContext(options);

        var entity = dbContext.Model.FindEntityType(typeof(User))!;
        Assert.Equal("users", entity.GetTableName());
        Assert.Equal("identity", entity.GetSchema());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.GetDatabaseName() == "ux_identity_users_single_owner");
    }
}
