using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ConfigurationRoundtripPreservesBudgetsAndRejectsStaleRevision()
    {
        var conversation = Conversation.Create("test", "demo", "initial");
        var defaults = new ChatOptions { ContextTokens = 4096 };
        Assert.Same(defaults, ConversationConfiguration.Options(conversation, defaults));
        conversation.Configure("other", "new system", "project facts", ConversationConfiguration.Serialize(
            new ChatOptions { ContextTokens = 2048, MaxOutputTokens = 256, Temperature = 0.2, Seed = 42 }));
        var options = ConversationConfiguration.Options(conversation, defaults);
        Assert.Equal(2048, options.ContextTokens);
        Assert.Equal(42, options.Seed);
        Assert.Equal(0.2, options.Temperature);
        Assert.Contains("project facts", ConversationConfiguration.System(conversation));
        Assert.Throws<ChatConflictException>(() => ConversationConfiguration.CheckRevision(conversation, 0));
        Assert.Equal(0, conversation.MessageCount);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void RejectsInvalidTemperature(double temperature) =>
        Assert.Throws<ArgumentException>(() => new ChatOptions { Temperature = temperature }.Validate());

    [Fact]
    public void PinnedFilesSurviveWindowAndExcludedFilesNeverEnterPrompt()
    {
        var files = new[]
        {
            new Attachment { Name = "old.txt", Text = "PINNED", AnchorSequence = 1, ContextMode = "pinned" },
            new Attachment { Name = "secret.txt", Text = "EXCLUDED", AnchorSequence = 9, ContextMode = "excluded" },
            new Attachment { Name = "expired.txt", Text = "EXPIRED", AnchorSequence = 1 }
        };
        var options = new ChatOptions { MaxHistoryTurns = 0 };
        var window = FileContext.Build("system", [], "question", options, files, 9);
        Assert.Contains("PINNED", window.Messages.Last().Content);
        Assert.DoesNotContain("EXCLUDED", window.Messages.Last().Content);
        Assert.DoesNotContain("EXPIRED", window.Messages.Last().Content);
        Assert.Equal(1, window.FilesIncluded);
        Assert.True(window.Messages.Sum(m => SlidingContext.EstimateTokens(m.Content))
            <= options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens);
    }

    [Fact]
    public void RejectsIncompleteHistoryBeforeConstructingPrompt() =>
        Assert.Throws<InvalidOperationException>(() => SlidingContext.Build("", [
            new ChatMessage(Guid.NewGuid(), 1, Guid.NewGuid(), "user", "orphan")], "question", new()));
}
