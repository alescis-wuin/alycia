using Alycia.Modules.Retrieval.Onnx;

namespace Alycia.Modules.Retrieval.Tests;

public sealed class NativeRerankerTests
{
    [Fact]
    public async Task Missing_weights_never_yield_an_invented_score()
    {
        using var provider = new OnnxPassageReranker(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ScoreAsync("question", [], default));
    }
}
