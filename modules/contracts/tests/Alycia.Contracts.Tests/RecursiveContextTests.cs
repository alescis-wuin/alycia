using Alycia.Contracts.Documents;

namespace Alycia.Modules.Chat.Tests;

public sealed class RecursiveContextTests
{
    [Fact]
    public void UnicodeChunkingKeepsEveryCharacterAndItsByteBound()
    {
        var text = string.Concat(Enumerable.Repeat("é🙂漢字\n", 100));
        var chunks = Utf8Text.Chunks(text, 37).ToArray();
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(c), 1, 37));
    }
}
