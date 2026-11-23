namespace Alycia.Modules.Chat.Tests;

public sealed class KnowledgePipelineTests
{
    [Fact]
    public void FacetsPreserveConstraintsAndAnUnexaminedCandidateKeepsItsRank()
    {
        const string question = "Compare Atlas 2.1 puis cherche QuaiMax sans réseau.";
        var plan = QueryPlanning.Build(question, [], [], 4, true);
        var brief = plan.Brief!;
        Assert.Equal(question, brief.Original);
        Assert.Equal(2, brief.Facets.Length);
        Assert.All(brief.Facets, f => { Assert.Contains("sans réseau", f.Semantic); Assert.Contains("sans réseau", f.Lexical); });
        Assert.Contains("2.1", brief.Literals);
        SourceChunk Chunk(string content) => new(Guid.NewGuid(), "", MemoryTransfer.Hash(content), "", "text", 0, content.Length, 1, 1, content, [], true);
        var examined = Chunk("Aucune donnée exploitable.");
        var next = Chunk("Atlas version 2.1");
        var classified = PassageClassifier.Classify(question, brief, [examined, next], PassageRankingSettings.Capture(new ChatSettings { ClassificationBudget = 4 }, null));
        Assert.Equal(4, classified.Report.Pairs);
        Assert.Equal(1, classified.Report.Unexamined);
        Assert.Equal("irrelevant", Assert.Single(classified.Report.Candidates).Relevance);
        Assert.Same(next, Assert.Single(classified.Ranked));
    }
}
