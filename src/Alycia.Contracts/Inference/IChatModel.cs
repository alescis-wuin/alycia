using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Inference;

public interface IChatModel
{
    Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct) => Task.FromResult(new ModelTokenization(null));
    async Task<ModelReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall options, CancellationToken ct) => new(await GenerateAsync(model, messages, options, ct));
    Task<long?> ResidentVramAsync(string model, CancellationToken ct) => Task.FromResult<long?>(null);
    Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => Task.FromResult(false);
    Task<ModelReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages,
        InferenceCall settings, Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        throw new ChatModelException("Ce fournisseur ne prend pas en charge les outils.");
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken cancellationToken);
    // Providers enforce settings.TimeoutSeconds after acquiring their inference slot; ct cancels queueing too.
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct)
        => GenerateAsync(model, messages, ct);
    async Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages,
        InferenceCall settings, Func<ModelDelta, Task> onDelta, CancellationToken ct)
    {
        var content = await GenerateAsync(model, messages, settings, ct);
        await onDelta(new(content));
        return new(content);
    }
}

public interface IModelMeter : IChatModel
{
    void AddCalls(IEnumerable<ModelCallUsage> calls);
    GenerationUsage Snapshot(string? identity, ITextTokenCounter? counter);
}

public interface IModelMeterFactory
{
    IModelMeter Create(IChatModel model);
}
