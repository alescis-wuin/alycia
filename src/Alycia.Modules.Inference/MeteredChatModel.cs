namespace Alycia.Modules.Inference;

// Per operation, never static: simultaneous conversations cannot mix their measurements.
public sealed class MeteredChatModel(IChatModel inner) : IModelMeter
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<ModelCallUsage> _calls = new();
    public void AddCalls(IEnumerable<ModelCallUsage> calls) { foreach (var call in calls) _calls.Enqueue(call); }
    public GenerationUsage Snapshot(string? identity, ITextTokenCounter? counter) => new(_calls.ToArray(), identity, PromptTokens.Description(counter),
        _calls.All(c => c.Stats?.PromptTokens is not null) ? _calls.Sum(c => (long)c.Stats!.PromptTokens!.Value) : null,
        _calls.All(c => c.Stats?.OutputTokens is not null) ? _calls.Sum(c => (long)c.Stats!.OutputTokens!.Value) : null,
        _calls.Sum(c => c.ElapsedMilliseconds), _calls.All(c => c.Stats?.PromptTokens is not null && c.Stats.OutputTokens is not null));
    public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct) => inner.InspectAsync(model, tokenCounting, ct);
    public Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => inner.SupportsToolsAsync(model, ct);
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => inner.ListModelsAsync(ct);
    public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => GenerateAsync(model, messages, InferenceCall.Default, ct);
    public async Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall options, CancellationToken ct)
    {
        var input = PromptSnapshot.Capture(messages);
        return (await Measure(effective => inner.GenerateReplyAsync(model, input, effective, ct), model, input, options, ct)).Content;
    }

    public Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall options, Func<ModelDelta, Task> delta, CancellationToken ct)
    {
        var input = PromptSnapshot.Capture(messages);
        return MeasureStreaming((observer, effective) => inner.StreamAsync(model, input, effective, observer, ct), delta, model, input, options, ct);
    }

    public Task<ModelReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall options, Func<ModelDelta, Task> delta, CancellationToken ct)
    {
        var input = PromptSnapshot.Capture(messages);
        return MeasureStreaming((observer, effective) => inner.StreamWithToolsAsync(model, input, effective, observer, ct), delta, model, input, options, ct, "tools");
    }

    private Task<ModelReply> MeasureStreaming(Func<Func<ModelDelta, Task>, InferenceCall, Task<ModelReply>> call,
        Func<ModelDelta, Task> delta, string model, IReadOnlyList<PromptMessage> messages, InferenceCall options, CancellationToken ct, string? purpose = null)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew(); double? first = null;
        return Measure(effective => call(value =>
        {
            if (value.Content.Length > 0) first ??= timer.Elapsed.TotalMilliseconds;
            return delta(value);
        }, effective), model, messages, options, ct, purpose, () => first);
    }
    private async Task<ModelReply> Measure(Func<InferenceCall, Task<ModelReply>> call, string model, IReadOnlyList<PromptMessage> messages, InferenceCall options, CancellationToken ct, string? purpose = null, Func<double?>? firstContent = null)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        ModelReply? reply = null;
        GenerationStats? failureStats = null;
        var effective = InferenceWindow.ForCall(options, messages, purpose == "tools");
        try { reply = await call(effective); return reply; }
        catch (ChatModelException exception) { failureStats = exception.Stats; throw; }
        finally
        {
            var duration = timer.Elapsed.TotalMilliseconds;
            var vram = options.AdaptiveInferenceEnabled && reply is not null && !ct.IsCancellationRequested ? await inner.ResidentVramAsync(model, ct) : null;
            _calls.Enqueue(new(purpose ?? options.Generation.CallPurpose, PromptTokens.InputCost(messages, options.TokenCounter, purpose == "tools"), duration,
                reply?.Stats ?? failureStats, firstContent?.Invoke(), effective.Generation.ContextTokens, options.AdaptiveInferenceEnabled ? Environment.WorkingSet : null, vram));
        }
    }
}

public sealed class ModelMeterFactory : IModelMeterFactory
{
    public IModelMeter Create(IChatModel model) => new MeteredChatModel(model);
}
