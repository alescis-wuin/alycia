using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class SlidingContextTests
{
    [Fact]
    public void PreservesSystemAndCurrentMessageAndLatestCompleteTurns()
    {
        var history = Turns("old question", "old answer", "recent question", "recent answer");
        var window = SlidingContext.Build("system", history, "now", new ChatOptions { MaxHistoryTurns = 1 });
        Assert.Equal(["system", "recent question", "recent answer", "now"], window.Messages.Select(item => item.Content));
        Assert.Equal(["system", "user", "assistant", "user"], window.Messages.Select(item => item.Role));
        Assert.Equal(2, window.HistoryMessagesIncluded);
        Assert.Equal(4, history.Count); // Stored history is untouched.
    }

    [Fact]
    public void BudgetEvictsWholeOldTurns()
    {
        var history = Turns(new string('x', 700), "old", "recent", "answer");
        var options = new ChatOptions { ContextTokens = 1024, MaxOutputTokens = 128, SafetyTokens = 128 };
        var window = SlidingContext.Build("system", history, "current", options);
        Assert.Equal(2, window.HistoryMessagesIncluded);
        Assert.True(window.EstimatedInputTokens <= 768);
        Assert.Equal("recent", window.Messages[1].Content);
    }

    [Fact]
    public void DoesNotSkipAnOversizedRecentTurnToIncludeOlderTurns()
    {
        var window = SlidingContext.Build("", Turns("old", "old answer", new string('x', 900), "answer"),
            "now", new ChatOptions { ContextTokens = 1024, MaxOutputTokens = 128, SafetyTokens = 128 });
        Assert.Single(window.Messages);
        Assert.Equal(0, window.HistoryMessagesIncluded);
    }

    [Fact]
    public void RejectsOversizedCurrentInputInsteadOfSilentlyTruncating()
    {
        Assert.Throws<ArgumentException>(() => SlidingContext.Build("system", [], new string('x', 800),
            new ChatOptions { ContextTokens = 1024, MaxOutputTokens = 128, SafetyTokens = 128 }));
    }

    [Fact]
    public void ZeroWindowKeepsOnlySystemAndCurrentInput()
    {
        var window = SlidingContext.Build("system", Turns("question", "answer"), "now",
            new ChatOptions { MaxHistoryTurns = 0 });
        Assert.Equal(["system", "now"], window.Messages.Select(item => item.Content));
        Assert.True(SlidingContext.EstimateTokens("é🙂") > "é🙂".Length + 16);
    }

    private static List<ChatMessage> Turns(params string[] texts)
    {
        var id = Guid.NewGuid();
        var result = new List<ChatMessage>();
        for (var i = 0; i < texts.Length; i += 2)
        {
            var request = Guid.NewGuid();
            result.Add(new(id, i + 1, request, "user", texts[i]));
            result.Add(new(id, i + 2, request, "assistant", texts[i + 1]));
        }

        return result;
    }
}
