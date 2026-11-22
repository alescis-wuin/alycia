using System.Text.Json;

namespace Alycia.Modules.Chat.Tests;

public sealed class RetrievalDiagnosticsTests
{
    [Fact]
    public void RankingDiagnosticsRoundTripWithoutSourceContentAndRemainBackwardCompatible()
    {
        var report = new TurnDiagnostics(Retrieval: new("lexical", 2, 1, 1, [], 1, 4, 2,
            [new("S123456789012", Guid.NewGuid(), "hash", 0, 4, 0, "zero_lexical_score")], 1));
        var restored = TurnDiagnostics.Read(JsonSerializer.Serialize(report, ProjectToolCatalog.Json));
        Assert.Equal(1, restored!.Retrieval!.ZeroScoreChunks);
        Assert.Equal("zero_lexical_score", Assert.Single(restored.Retrieval.Decisions!).Reason);
        Assert.NotNull(TurnDiagnostics.Read("{\"retrieval\":{\"mode\":\"lexical\",\"candidateChunks\":0,\"indexedFiles\":0,\"eligibleFiles\":0,\"omissions\":[]}}"));
    }
}
