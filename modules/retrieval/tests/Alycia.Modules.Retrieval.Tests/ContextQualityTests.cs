
namespace Alycia.Modules.Chat.Tests;

public class ContextQualityTests
{
    [Fact]
    public void AnaphoraKeepsLiteralAndOnlyResolvesUnambiguousUserMentions()
    {
        var id = Guid.NewGuid();
        var turns = new[] { new QueryHistoryItem(id, 1, "user", "Ouvre src/A.cs"),
            new QueryHistoryItem(id, 2, "assistant", "Je propose B.cs.") };
        var plan = QueryPlanning.Build("Et ce fichier ?", turns, [], 3);
        Assert.Equal("Et ce fichier ?", plan.Original);
        Assert.Contains(plan.Queries, q => q is { Kind: "exact", Text: "src/A.cs" });
        Assert.StartsWith("H1:", Assert.Single(plan.Resolved).Reference);
        Assert.True(QueryPlanning.Build("Et cette version ?", [new(id, 3, "user", "A.cs et B.cs")], [], 3).Ambiguous);
        Assert.Empty(QueryPlanning.Build("Et avant ?", [], [], 3).Resolved);
        Assert.Equal(2, QueryPlanning.Build("Compare A.cs et B.cs", [], [], 2).Queries.Length);
    }

    [Theory]
    [InlineData("La rétention Orion est de 17 jours [SABCDEF123456].", "contradicted")]
    [InlineData("La rétention Orion est de 37 jours [SABCDEF123456].", "supported")]
    [InlineData("La rétention Orion est de 37 jours [S000000000000].", "insufficient")]
    [InlineData("Quelle est votre préférence ?", "not_verifiable")]
    public void VerificationDistinguishesEvidenceAndContradictions(string answer, string status)
    {
        var evidence = new EvidenceText("SABCDEF123456", "La conservation Orion est de 37 jours.");
        Assert.Equal(status, Assert.Single(SemanticClaimAudit.Check(answer, [], [evidence]).Findings).Status);
        var proposal = new EvidenceText("H2:123456ABCDEF", "Vous avez choisi Atlas.", "assistant");
        Assert.Equal("insufficient", Assert.Single(SemanticClaimAudit.Check("Vous avez choisi Atlas [H2:123456ABCDEF].", [], [proposal]).Findings).Status);
    }
}
