using System.Collections.Immutable;

namespace Alycia.Modules.Testing;

public sealed class MemoryMeterFactory : IModelMeterFactory
{
    public IModelMeter Create(IChatModel model) => new MemoryModelMeter(model);
}

// An operation-local meter with a deterministic clock. Model behavior remains injectable.
public sealed class MemoryModelMeter(IChatModel model) : IModelMeter
{
    private readonly List<ModelCallUsage> _calls = [];
    public void AddCalls(IEnumerable<ModelCallUsage> calls) { lock (_calls) _calls.AddRange(calls); }

    public GenerationUsage Snapshot(string? identity, ITextTokenCounter? counter)
    {
        ImmutableArray<ModelCallUsage> copy;
        lock (_calls) copy = [.. _calls];
        long? input = 0, output = 0;
        foreach (var call in copy) { input += call.Stats?.PromptTokens; output += call.Stats?.OutputTokens; }
        return new(copy, identity, PromptTokens.Description(counter), input, output,
            copy.Sum(c => c.ElapsedMilliseconds), input.HasValue && output.HasValue);
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => model.ListModelsAsync(ct);
    public Task<ModelTokenization> InspectAsync(string name, string mode, CancellationToken ct) => model.InspectAsync(name, mode, ct);
    public Task<bool> SupportsToolsAsync(string name, CancellationToken ct) => model.SupportsToolsAsync(name, ct);
    public Task<long?> ResidentVramAsync(string name, CancellationToken ct) => model.ResidentVramAsync(name, ct);
    public Task<string> GenerateAsync(string name, IReadOnlyList<PromptMessage> messages, CancellationToken ct) =>
        GenerateAsync(name, messages, InferenceCall.Default, ct);
    public async Task<string> GenerateAsync(string name, IReadOnlyList<PromptMessage> messages, InferenceCall call, CancellationToken ct) =>
        (await GenerateReplyAsync(name, messages, call, ct)).Content;
    public Task<ModelReply> GenerateReplyAsync(string name, IReadOnlyList<PromptMessage> messages, InferenceCall call, CancellationToken ct) =>
        Observe(messages, call, false, () => model.GenerateReplyAsync(name, messages, call, ct));
    public Task<ModelReply> StreamAsync(string name, IReadOnlyList<PromptMessage> messages, InferenceCall call, Func<ModelDelta, Task> observer, CancellationToken ct) =>
        Observe(messages, call, false, () => model.StreamAsync(name, messages, call, observer, ct));
    public Task<ModelReply> StreamWithToolsAsync(string name, IReadOnlyList<PromptMessage> messages, InferenceCall call, Func<ModelDelta, Task> observer, CancellationToken ct) =>
        Observe(messages, call, true, () => model.StreamWithToolsAsync(name, messages, call, observer, ct));

    private async Task<ModelReply> Observe(IReadOnlyList<PromptMessage> messages, InferenceCall call, bool tools, Func<Task<ModelReply>> execute)
    {
        var input = PromptTokens.InputCost(messages, call.TokenCounter, tools);
        ModelReply? reply = null;
        try { return reply = await execute(); }
        finally { AddCalls([new(tools ? "tools" : call.Generation.CallPurpose, input, 0, reply?.Stats)]); }
    }
}
