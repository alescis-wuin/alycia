namespace Alycia.Modules.Inference;

public sealed class ChatGenerationAdapter(ITextGeneration provider, InferenceCall defaults) : IChatModel
{
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => provider.ListModelsAsync(ct);
    public Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => provider.SupportsToolsAsync(model, ct);
    public Task<long?> ResidentVramAsync(string model, CancellationToken ct) => provider.ResidentVramAsync(model, ct);
    public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct) =>
        provider.InspectAsync(model, tokenCounting, ct);
    public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) =>
        GenerateAsync(model, messages, defaults, ct);
    public async Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct) =>
        (await GenerateReplyAsync(model, messages, settings, ct)).Content;
    public Task<ModelReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct) =>
        provider.GenerateAsync(model, messages, settings.Generation, ct);
    public Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
        Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        provider.StreamAsync(model, messages, settings.Generation, onDelta, false, ct);
    public Task<ModelReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
        Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        provider.StreamAsync(model, messages, settings.Generation, onDelta, true, ct);
}
