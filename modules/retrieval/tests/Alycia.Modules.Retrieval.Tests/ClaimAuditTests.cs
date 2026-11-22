
namespace Alycia.Modules.Chat.Tests;

public sealed class ClaimAuditTests
{
    [Theory]
    [InlineData("Le quota vaut 37. [S123456789012]", "direct_quote")]
    [InlineData("Le quota vaut 38. [S123456789012]", "uncertain")]
    [InlineData("Le quota vaut 37. [S000000000000]", "not_demonstrated")]
    [InlineData("Le quota vaut 37.", "not_demonstrated")]
    public void KnownMarkerAloneNeverEstablishesTheClaim(string answer, string status)
    {
        var result = ClaimAudit.Check(answer, [], [new("S123456789012", "Le quota vaut 37.")]);
        Assert.Equal(status, Assert.Single(result.Findings).Status);
        Assert.Contains("do not establish truth", result.Policy);
    }
}
