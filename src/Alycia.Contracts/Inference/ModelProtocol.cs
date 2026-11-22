using Alycia.Contracts.Chat;
using System.Text.Json.Serialization;

namespace Alycia.Contracts.Inference;

public sealed record PromptMessage(string Role, string Content,
    [property: JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ModelToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Thinking = null);

public enum ChatModelFailure { Other, EmptyContent, Timeout }
public sealed class ChatModelException(string message, GenerationStats? stats = null, ChatModelFailure failure = ChatModelFailure.Other) : Exception(message)
{
    public GenerationStats? Stats { get; } = stats;
    public ChatModelFailure Failure { get; } = failure;
}
