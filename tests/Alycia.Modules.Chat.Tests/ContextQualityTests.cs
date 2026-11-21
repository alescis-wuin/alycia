using Alycia.Modules.Inference;
using Alycia.Contracts.Retrieval;
using Alycia.Modules.Retrieval;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public class ContextQualityTests
{
    [Fact]
    public void AnaphoraKeepsLiteralAndOnlyResolvesUnambiguousUserMentions()
    {
        var id = Guid.NewGuid(); var request = Guid.NewGuid();
        var turns = new[] { new ChatMessage(id, 1, request, "user", "Ouvre src/A.cs"),
            new ChatMessage(id, 2, request, "assistant", "Je propose B.cs.") };
        var plan = QueryPlanning.Build("Et ce fichier ?", turns.Select(m => new QueryHistoryItem(m.ConversationId, m.Sequence, m.Role, m.Content)).ToArray(), [], 3);
        Assert.Equal("Et ce fichier ?", plan.Original);
        Assert.Contains(plan.Queries, q => q is { Kind: "exact", Text: "src/A.cs" });
        Assert.StartsWith("H1:", Assert.Single(plan.Resolved).Reference);
        Assert.True(QueryPlanning.Build("Et cette version ?", [new(id, 3, "user", "A.cs et B.cs")], [], 3).Ambiguous);
        Assert.Empty(QueryPlanning.Build("Et avant ?", [], [], 3).Resolved);
        Assert.Equal(2, QueryPlanning.Build("Compare A.cs et B.cs", [], [], 2).Queries.Length);
    }

    [Fact]
    public void MmrKeepsBothPinsAndOriginalCoordinatesWithinFramedBudget()
    {
        var files = new[] { "a.md", "b.md", "interdit.md" }.Select((name, n) =>
            new Attachment { Id = Guid.NewGuid(), Name = name, Text = new string('x', 1800), Sha256 = name, ContextMode = n < 2 ? "pinned" : "excluded" }).ToArray();
        var chunks = files.Select(f => new SourceChunk(f.Id, f.Name, f.Sha256, "section", "text", 0, f.Text.Length, 1, 1, f.Text, [], true)).ToArray();
        var options = new ChatOptions { EvidenceSelectionEnabled = true };
        var packed = ProjectRetrieval.Pack(chunks, files.Select(f => f.ToTurnFile()).ToArray(), 750, options);
        Assert.Equal(2, packed.Sources.Count);
        Assert.Equal(TokenBudget.Count(packed.Text, options), packed.Assembly!.Used);
        Assert.InRange(packed.Assembly.Used, 1, 750);
        foreach (var source in packed.Sources)
        {
            Assert.True(source.Partial);
            Assert.Equal(ProjectRetrieval.Reference(source.Id, source.Sha256!, 0, source.End!.Value), source.Reference);
            Assert.Equal(files.Single(f => f.Id == source.Id).Text[..source.End.Value], source.Content);
        }
        Assert.DoesNotContain("interdit", packed.Text);
        Assert.Throws<ArgumentException>(() => ProjectRetrieval.Pack(chunks, files.Select(f => f.ToTurnFile()).ToArray(), 10, options));
    }

    [Fact]
    public void ParentIsSearchableButIsNeverAddedToTheOriginalQuote()
    {
        const string text = "# Atlas\n## Archives\n### Conservation\n37 jours\n";
        var parts = CodeStructure.Split(Guid.NewGuid(), "index.md", "sha", text, hierarchical: true);
        var leaf = parts.Single(c => c.Symbol.Contains("Conservation"));
        Assert.Contains("Atlas / Archives / Conservation", leaf.IndexContext);
        Assert.Equal(text[leaf.Start..leaf.End], leaf.Text);
        Assert.DoesNotContain("Atlas", leaf.Text);
        Assert.Contains("Archives", EmbeddingChunks.Text(leaf));
        Assert.NotEqual(EmbeddingChunks.Key(leaf), EmbeddingChunks.Key(leaf with { IndexContext = null }));
        var options = new ChatOptions { HierarchicalIndexEnabled = true, EmbeddingQueryPrefix = "query: " };
        var space = EmbeddingSettings.From(options);
        Assert.NotEqual(space.VectorSpace("m:digest", 256), space.VectorSpace("m:digest", 768));
        Assert.NotEqual(space.VectorSpace("m:digest", 256), (space with { DocumentPrefix = "passage: " }).VectorSpace("m:digest", 256));
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

    [Fact]
    public async Task PlanStopsAtLimitAndSeparatesUnsearchedFromEmpty()
    {
        var options = new ChatOptions
        {
            AgentPlanningEnabled = true,
            AgentPlanMaxCalls = 1,
            ContextTokens = 32768,
            SearchPlan = QueryPlanning.Build("Compare A.cs et B.cs", [], [], 3)
        };
        var model = new FinalModel(); var tools = new EmptyTools();
        var result = await ProjectAgent.RunAsync(model, tools, Guid.NewGuid(), "m", new([new("user", "Compare A.cs et B.cs")], 0, 40), options, null, default);
        Assert.Equal("Réponse limitée", result.Content);
        Assert.Equal("call_limit", result.Research!.StopReason);
        Assert.Equal(new[] { "not_found", "not_searched" }, result.Research.Goals.Select(g => g.State));
        Assert.Single(result.Tools!);
    }

    [Fact]
    public void EffectiveWindowProtectsSchemasOutputAndSafety()
    {
        var options = new ChatOptions { ContextTokens = 8192, MaxOutputTokens = 300, SafetyTokens = 256, GenerateUntilStop = false, AdaptiveInferenceEnabled = true };
        PromptMessage[] input = [new("user", "Bonjour")];
        Assert.Equal(1024, InferenceWindow.ForCall(options.InferenceCall(), input, false).Generation.ContextTokens);
        var tools = InferenceWindow.ForCall(options.InferenceCall(), input, true);
        Assert.InRange(tools.Generation.ContextTokens, PromptTokens.InputCost(input, options.TokenCounter, true) + 556, 8192);
        Assert.Throws<ArgumentException>(() => InferenceWindow.ForCall(options.InferenceCall(), [new("user", new string('a', 8000))], false));
        options.GenerateUntilStop = true;
        Assert.Equal(8192, InferenceWindow.ForCall(options.InferenceCall(), input, false).Generation.ContextTokens);
    }

    private sealed class FinalModel : IChatModel
    {
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => Task.FromResult("Réponse limitée");
    }
    private sealed class EmptyTools : IProjectTools
    {
        public Task<bool> HasSourcesAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement args, int budget, CancellationToken ct) =>
            Task.FromResult(new ProjectToolResult("""{"results":[],"nextOffset":null}"""));
    }
}
