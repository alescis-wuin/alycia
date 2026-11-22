using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Modules.Context;

namespace Alycia.Modules.Chat.Tests;

public sealed class FileTests
{
    [Fact]
    public void FilesFollowSlidingWindowAndRespectBudgetAndRoles()
    {
        var id = Guid.NewGuid();
        var request = Guid.NewGuid();
        var history = new[] { new HistoryMessage(id, 1, request, "user", "question", DateTimeOffset.UtcNow), new HistoryMessage(id, 2, request, "assistant", "answer", DateTimeOffset.UtcNow) };
        var old = ContextTestInputs.File() with { Id = Guid.NewGuid(), Name = "old.txt", Text = "old content", AnchorSequence = 1 };
        var current = ContextTestInputs.File() with { Id = Guid.NewGuid(), Name = "new.txt", Text = new string('é', 10000), AnchorSequence = 3, CreatedAtUtc = DateTimeOffset.UtcNow };
        var options = new ChatSettings { MaxHistoryTurns = 0 };
        var result = FileContext.Build(new Alycia.Modules.Context.SlidingHistory(), "system", history, "question", options.Context(), [old, current], 3);
        Assert.Equal("system", result.Messages[0].Content);
        Assert.Equal("user", result.Messages[^1].Role);
        Assert.DoesNotContain("old content", result.Messages[^1].Content);
        Assert.Contains("new.txt", result.Messages[^1].Content);
        Assert.Equal(1, result.FilesIncluded);
        Assert.True(result.Messages.Sum(m => SlidingContext.EstimateTokens(m.Content))
            <= options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens);
        var next = FileContext.Build(new Alycia.Modules.Context.SlidingHistory(), "system", [], "later", options.Context(), [current], 5);
        Assert.Equal(0, next.FilesIncluded);
        Assert.DoesNotContain("new.txt", next.Messages[^1].Content);
        options.MaxHistoryTurns = 1;
        var recent = new[] { new HistoryMessage(id, 5, request, "user", new string('a', 1700), DateTimeOffset.UtcNow), new HistoryMessage(id, 6, request, "assistant", new string('b', 1700), DateTimeOffset.UtcNow) };
        var expired = FileContext.Build(new Alycia.Modules.Context.SlidingHistory(), "system", recent, "later", options.Context(), [old], 7);
        Assert.Equal(2, expired.HistoryMessagesIncluded);
        Assert.Equal(0, expired.FilesIncluded);
    }
}
