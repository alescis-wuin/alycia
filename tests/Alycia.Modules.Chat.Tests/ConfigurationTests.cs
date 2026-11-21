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
        Assert.Equal(defaults.ContextTokens, ConversationConfiguration.Options(conversation, defaults).ContextTokens);
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

    [Fact]
    public void UnconfiguredConversationsDoNotShareMutableDefaultOptions()
    {
        var defaults = new ChatOptions { ContextTokens = 4096, Temperature = 0.25 };
        var first = Conversation.Create("Première", "demo", "Instruction");
        var second = Conversation.Create("Deuxième", "demo", "Instruction");

        var firstOptions = ConversationConfiguration.Options(first, defaults);
        var secondOptions = ConversationConfiguration.Options(second, defaults);
        Assert.NotSame(defaults, firstOptions);
        Assert.NotSame(firstOptions, secondOptions);

        firstOptions.ContextTokens = 16384;
        firstOptions.Temperature = 1.5;
        Assert.Equal(4096, defaults.ContextTokens);
        Assert.Equal(0.25, defaults.Temperature);
        Assert.Equal(4096, secondOptions.ContextTokens);
        Assert.Equal(0.25, secondOptions.Temperature);
        Assert.Equal(4096, ConversationConfiguration.Options(first, defaults).ContextTokens);
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
        var window = FileContext.Build(new Alycia.Modules.Context.SlidingHistory(), "system", [], "question", options, files, 9);
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
            new ChatMessage(Guid.NewGuid(), 1, Guid.NewGuid(), "user", "orphan")], "question", new()));
}
