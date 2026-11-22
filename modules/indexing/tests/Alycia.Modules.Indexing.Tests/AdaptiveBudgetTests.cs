using Alycia.Modules.Indexing;

namespace Alycia.Modules.Chat.Tests;

public sealed class AdaptiveBudgetTests
{
    [Fact]
    public void EmbeddingChunksPreserveEveryUnicodeCharacterAndCoordinates()
    {
        var text = string.Concat(Enumerable.Repeat("é🙂漢字\n", 2000));
        var chunk = new SourceChunk(Guid.NewGuid(), "a.txt", "sha", "text", "text", 10, 10 + text.Length, 4, 2004, text, [], true);
        var parts = EmbeddingChunks.Split(chunk, 129).ToArray();
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text)));
        Assert.All(parts, p => { Assert.False(char.IsLowSurrogate(p.Text[0])); Assert.False(char.IsHighSurrogate(p.Text[^1])); });
        Assert.Equal(chunk.Start, parts[0].Start); Assert.Equal(chunk.End, parts[^1].End);
        Assert.Equal(parts.Length, parts.Select(EmbeddingChunks.Key).Distinct().Count());
        Assert.NotEqual(EmbeddingChunks.Key(parts[0]), EmbeddingChunks.Key(parts[0] with { Sha256 = "new" }));
    }
}
