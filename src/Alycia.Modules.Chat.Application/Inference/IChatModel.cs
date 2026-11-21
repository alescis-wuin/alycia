using System.Text.Json.Serialization;

namespace Alycia.Modules.Chat.Application;

public sealed record GenerationReply(string Content, GenerationStats? Stats = null, IReadOnlyList<ModelToolCall>? ToolCalls = null, string? Thinking = null, IReadOnlyList<ToolObservation>? Tools = null, IReadOnlyList<string>? Warnings = null,
    [property: JsonIgnore] IReadOnlyList<EvidenceText>? EvidenceTexts = null, ResearchReport? Research = null);

public interface IChatModel
{
    Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions options, CancellationToken ct) => Task.FromResult(options.Copy());
    async Task<GenerationReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct) => new(await GenerateAsync(model, messages, options, ct));
    Task<long?> ResidentVramAsync(string model, CancellationToken ct) => Task.FromResult<long?>(null);
    Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => Task.FromResult(false);
    Task<GenerationReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        throw new ChatModelException("Ce fournisseur ne prend pas en charge les outils.");
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken cancellationToken);
    // Providers enforce settings.TimeoutSeconds after acquiring their inference slot; ct cancels queueing too.
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings, CancellationToken ct)
        => GenerateAsync(model, messages, ct);
    async Task<GenerationReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken ct)
    {
        var content = await GenerateAsync(model, messages, settings, ct);
        await onDelta(new(content));
        return new(content);
    }
}
