namespace Alycia.Modules.Testing;

// Each instance owns its script. No mutable scenario is shared across callers.
public sealed class ScriptedTextGeneration : ITextGeneration, IChatModel
{
    public const string Model = "alycia-mock";
    private readonly string[] _chunks;
    private readonly string? _identity;
    private readonly int? _failAfter;
    private readonly Func<int, CancellationToken, Task>? _beforeDelta;

    public ScriptedTextGeneration(IEnumerable<string>? chunks = null, int? failAfter = null, Func<int, CancellationToken, Task>? beforeDelta = null, string? identity = null)
    {
        _chunks = (chunks ?? ["Bon", "jour"]).ToArray();
        _failAfter = failAfter;
        _beforeDelta = beforeDelta;
        _identity = identity;
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([Model]);
    }

    public Task<bool> SupportsToolsAsync(string model, CancellationToken ct)
    {
        Validate(model, ct);
        return Task.FromResult(false);
    }

    public Task<long?> ResidentVramAsync(string model, CancellationToken ct)
    {
        Validate(model, ct);
        return Task.FromResult<long?>(null);
    }

    public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct)
    {
        Validate(model, ct);
        return Task.FromResult(new ModelTokenization(_identity, Inspected: _identity is not null));
    }

    public Task<ModelReply> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, GenerationParameters parameters, CancellationToken ct) =>
        StreamAsync(model, messages, parameters, _ => Task.CompletedTask, false, ct);

    public async Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, GenerationParameters parameters,
        Func<ModelDelta, Task> onDelta, bool useTools, CancellationToken ct)
    {
        Validate(model, ct);
        if (useTools) throw new ChatModelException("Outils indisponibles pour le modèle simulé.");
        for (var i = 0; i < _chunks.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (_beforeDelta is not null) await _beforeDelta(i, ct);
            ct.ThrowIfCancellationRequested();
            if (_failAfter == i) throw new ChatModelException("Interruption simulée.");
            await onDelta(new(_chunks[i]));
        }
        ct.ThrowIfCancellationRequested();
        return new(string.Concat(_chunks));
    }

    async Task<string> IChatModel.GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) =>
        (await GenerateAsync(model, messages, InferenceCall.Default.Generation, ct)).Content;
    async Task<string> IChatModel.GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct) =>
        (await GenerateAsync(model, messages, settings.Generation, ct)).Content;
    Task<ModelReply> IChatModel.GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct) =>
        GenerateAsync(model, messages, settings.Generation, ct);
    Task<ModelReply> IChatModel.StreamAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
        Func<ModelDelta, Task> onDelta, CancellationToken ct) => StreamAsync(model, messages, settings.Generation, onDelta, false, ct);
    Task<ModelReply> IChatModel.StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
        Func<ModelDelta, Task> onDelta, CancellationToken ct) => StreamAsync(model, messages, settings.Generation, onDelta, true, ct);

    private static void Validate(string model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (model != Model) throw new ChatModelException("Modèle simulé indisponible.");
    }
}
