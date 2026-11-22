using Alycia.Contracts.Chat;
using Alycia.Modules.Context;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectRetrievalTests
{
    [Fact]
    public void CompositeContextKeepsOriginalEvidenceBesideBoundedOrientation()
    {
        var c = Chunk("code.cs", "Store", "orion quota 37");
        var file = ContextTestInputs.File() with { Id = c.FileId, Name = c.Path, Text = c.Text, Sha256 = c.Sha256, ContextMode = "project" };
        var summary = ContextTestInputs.File() with { Id = Guid.NewGuid(), Name = "Synthèse", Text = new string('x', 5000), Sha256 = "derived", ContextMode = "pinned", Status = "derived_summary" };
        var result = PassageContext.Pack([c], [file, summary], 2000, new ChatSettings().Context());
        Assert.Contains(result.Sources, s => s.Id == file.Id);
        Assert.InRange(result.Sources.Single(s => s.Id == summary.Id).Utf8Bytes, 1, 500);
        Assert.True(PromptTokens.Count(result.Text, null) <= 2000);
    }

    [Fact]
    public void PartialRangeDoesNotClaimUnreadLines()
    {
        var text = string.Join('\n', Enumerable.Range(0, 100).Select(i => "source line " + i));
        var c = Chunk("src/A.txt", "data", text) with { EndLine = 100 };
        var files = new[] { ContextTestInputs.File() with { Id = c.FileId, Name = c.Path, Sha256 = c.Sha256, Text = text, ContextMode = "project" } };
        var result = PassageContext.Pack([c], files, 600, new ChatSettings().Context());
        Assert.Single(result.Sources);
        Assert.True(result.Sources[0].Partial);
        Assert.True(result.Sources[0].EndLine < 100);
        Assert.DoesNotContain(":L1-L100", result.Text);
    }

    private static SourceChunk Chunk(string path, string symbol, string text) => new(Guid.NewGuid(), path, "hash", symbol, "method", 0, text.Length, 1, 1, text, [], true);
}
