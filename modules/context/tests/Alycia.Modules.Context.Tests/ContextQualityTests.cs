using Alycia.Contracts.Chat;
using Alycia.Modules.Context;

namespace Alycia.Modules.Chat.Tests;

public sealed class ContextQualityTests
{
    [Fact]
    public void MmrKeepsBothPinsAndOriginalCoordinatesWithinFramedBudget()
    {
        var files = new[] { "a.md", "b.md", "interdit.md" }.Select((name, n) =>
            ContextTestInputs.File() with { Id = Guid.NewGuid(), Name = name, Text = new string('x', 1800), Sha256 = name, ContextMode = n < 2 ? "pinned" : "excluded" }).ToArray();
        var chunks = files.Select(f => new SourceChunk(f.Id, f.Name, f.Sha256, "section", "text", 0, f.Text.Length, 1, 1, f.Text, [], true)).ToArray();
        var options = new ChatSettings { EvidenceSelectionEnabled = true };
        var packed = PassageContext.Pack(chunks, files, 750, options.Context());
        Assert.Equal(2, packed.Sources.Count);
        Assert.Equal(PromptTokens.Count(packed.Text, null), packed.Assembly!.Used);
        Assert.InRange(packed.Assembly.Used, 1, 750);
        foreach (var source in packed.Sources)
        {
            Assert.True(source.Partial);
            Assert.Equal(SourceCoordinates.Reference(source.Id, source.Sha256!, 0, source.End!.Value), source.Reference);
            Assert.Equal(files.Single(f => f.Id == source.Id).Text[..source.End.Value], source.Content);
        }
        Assert.DoesNotContain("interdit", packed.Text);
        Assert.Throws<ArgumentException>(() => PassageContext.Pack(chunks, files, 10, options.Context()));
    }
}
