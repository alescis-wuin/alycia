using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Modules.Context;

namespace Alycia.Modules.Chat.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void PinnedFilesSurviveWindowAndExcludedFilesNeverEnterPrompt()
    {
        var files = new[]
        {
            ContextTestInputs.File() with { Name = "old.txt", Text = "PINNED", AnchorSequence = 1, ContextMode = "pinned" },
            ContextTestInputs.File() with { Name = "secret.txt", Text = "EXCLUDED", AnchorSequence = 9, ContextMode = "excluded" },
            ContextTestInputs.File() with { Name = "expired.txt", Text = "EXPIRED", AnchorSequence = 1 }
        };
        var options = new ChatSettings { MaxHistoryTurns = 0 };
        var window = FileContext.Build(new Alycia.Modules.Context.SlidingHistory(), "system", [], "question", options.Context(), files, 9);
        Assert.Contains("PINNED", window.Messages.Last().Content);
        Assert.DoesNotContain("EXCLUDED", window.Messages.Last().Content);
        Assert.DoesNotContain("EXPIRED", window.Messages.Last().Content);
        Assert.Equal(1, window.FilesIncluded);
        Assert.True(window.Messages.Sum(m => SlidingContext.EstimateTokens(m.Content))
            <= options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens);
    }

    [Fact]
    public void RejectsIncompleteHistoryBeforeConstructingPrompt() =>
        Assert.Throws<InvalidOperationException>(() => SlidingContext.Build(new Alycia.Modules.Context.SlidingHistory(), "", [
            new HistoryMessage(Guid.NewGuid(), 1, Guid.NewGuid(), "user", "orphan", DateTimeOffset.UtcNow)], "question", new ChatSettings().Context()));
}
