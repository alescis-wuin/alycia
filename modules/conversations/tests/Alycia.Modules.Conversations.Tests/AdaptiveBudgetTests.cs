using Alycia.Modules.Chat.Application;
using System.Text.Json;

namespace Alycia.Modules.Chat.Tests;

public sealed class AdaptiveBudgetTests
{
    [Fact]
    public void NewSettingsRoundTripWithoutLosingExplicitValues()
    {
        var options = new ChatOptions
        {
            ToolMaxCalls = 300,
            ToolContextTokens = 4096,
            ToolResultBytes = 131072,
            ToolTimeoutSeconds = 3600,
            DocumentContextTokens = 2048,
            EmbeddingBatchSize = 2,
            EmbeddingChunkCharacters = 512,
            EmbedHistory = false,
            IndexMaxCharacters = 0
        };
        var json = ConversationConfiguration.Serialize(options);
        var copy = JsonSerializer.Deserialize<ChatOptions>(json, ProjectToolCatalog.Json)!;
        Assert.Equal(json, ConversationConfiguration.Serialize(copy));
        Assert.Equal(0, new ChatOptions().ToolMaxCalls);
        Assert.Equal(0, new ChatOptions().IndexMaxCharacters);
    }
}
