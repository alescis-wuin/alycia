using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Inference;

public sealed record ModelTokenization(string? Identity, bool Inspected = false,
    ITextTokenCounter? Counter = null, int? ContextLimit = null);

public sealed record ModelReply(string Content, GenerationStats? Stats = null,
    IReadOnlyList<ModelToolCall>? ToolCalls = null, string? Thinking = null);

public interface ITextGeneration
{
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
    Task<bool> SupportsToolsAsync(string model, CancellationToken ct);
    Task<long?> ResidentVramAsync(string model, CancellationToken ct);
    Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct);
    Task<ModelReply> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, GenerationParameters parameters, CancellationToken ct);
    Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, GenerationParameters parameters,
        Func<ModelDelta, Task> onDelta, bool useTools, CancellationToken ct);
}
