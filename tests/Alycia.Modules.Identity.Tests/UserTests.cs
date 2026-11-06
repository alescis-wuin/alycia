using Alycia.Modules.Identity.Domain;

namespace Alycia.Modules.Identity.Tests;

public sealed class UserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("bad name")]
    [InlineData("name@example.com")]
    [InlineData("élise")]
    public void Create_RejectsInvalidLogin(string? login)
    {
        Assert.Throws<ArgumentException>(() => User.Create(login));
    }

    [Fact]
    public void Create_NormalizesAsciiLoginAndAllocatesGuid()
    {
        var user = User.Create("  Alice_1.x  ");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("ALICE_1.X", user.LoginIdentifier);
    }

    [Fact]
    public void Create_AcceptsThreeAndSixtyFourCharacters()
    {
        Assert.Equal("ABC", User.Create("abc").LoginIdentifier);
        Assert.Equal(new string('A', 64), User.Create(new string('a', 64)).LoginIdentifier);
    }

    [Fact]
    public void Create_RejectsSixtyFiveCharactersAndEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => User.Create(new string('x', 65)));
        Assert.Throws<ArgumentException>(() => new User(Guid.Empty, "owner"));
    }

    [Fact]
    public void SetPasswordHash_RequiresNonEmptyVerifier()
    {
        var user = User.Create("owner");

        Assert.Throws<ArgumentException>(() => user.SetPasswordHash(null));
        Assert.Throws<ArgumentException>(() => user.SetPasswordHash(" "));

        user.SetPasswordHash("opaque-hasher-output-for-test");
        Assert.Equal("opaque-hasher-output-for-test", user.PasswordHash);
    }
}
