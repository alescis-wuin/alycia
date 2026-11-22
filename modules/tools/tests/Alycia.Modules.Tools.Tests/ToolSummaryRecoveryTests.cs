using Alycia.Modules.Tools;
using System.Text;

namespace Alycia.Modules.Chat.Tests;

public sealed class ToolSummaryRecoveryTests
{
    [Fact]
    public void ExtractiveFallbackIsBoundedUnicodeAndExplicitAboutOmissions()
    {
        var text = string.Concat(Enumerable.Repeat("é🙂漢字", 100));
        var extract = ToolMemoryFallback.Extract([new("Mémoire", text), new("src/A.cs", text)], 512);
        Assert.InRange(Encoding.UTF8.GetByteCount(extract), 1, 512);
        Assert.StartsWith("REPLI EXTRACTIF : portions omises", extract);
        Assert.Contains("[src/A.cs]", extract);
        Assert.Contains("é🙂漢字", extract);
        Assert.DoesNotContain("\uFFFD", extract);
    }
}
