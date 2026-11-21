using System.Text;
using System.Text.Json.Serialization;

namespace Alycia.Modules.Chat.Application;

public static class TokenBudget
{
    public static GenerationUsage? ReadUsage(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("usage", out var usage)
                ? System.Text.Json.JsonSerializer.Deserialize<GenerationUsage>(usage.GetRawText(), ProjectToolCatalog.Json) : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
    // Framing is an explicit conservative allowance, not a claim of exact template rendering.
    public static int Count(string text, ChatOptions options) => PromptTokens.Count(text, options.TokenCounter);
    public static string Description(ChatOptions options) => PromptTokens.Description(options.TokenCounter);
}
