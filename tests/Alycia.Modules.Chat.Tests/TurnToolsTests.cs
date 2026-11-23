using Alycia.Modules.Testing;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Contracts.Tools;

namespace Alycia.Modules.Chat.Tests;

public sealed class TurnToolsTests
{
    [Theory]
    [InlineData(false, false, "sync")]
    [InlineData(false, true, "stream")]
    [InlineData(true, false, "agent")]
    [InlineData(true, true, "agent")]
    public async Task PresenceOfActiveToolsAndConsumerSelectsTheGenerationPath(bool active, bool streaming, string expected)
    {
        var scope = new ToolTurnScope(Guid.NewGuid(), null, "demo");
        var window = new ContextWindow([new("user", "Bonjour")], 0, 10);
        var deltas = new List<string>();
        Task OnDelta(ModelDelta delta)
        {
            deltas.Add(delta.Content);
            return Task.CompletedTask;
        }

        var reply = await new TurnTools(RecursiveSummarizer.Create).GenerateAsync(new Model(true), active ? new Sources(true) : null,
            scope, window.ToolPrompt(), new ChatOptions().ToolGeneration(), streaming ? OnDelta : null, default);

        Assert.Equal(expected, reply.Content);
        if (streaming) Assert.Equal([expected], deltas);
    }

    private sealed class Sources(bool available) : ISnapshotProjectTools
    {
        public List<Guid> Probes { get; } = [];
        public Task<bool> HasSourcesAsync(Guid id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Probes.Add(id);
            return Task.FromResult(available);
        }

        public Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement arguments, int budget, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class Model(bool supported) : IChatModel
    {
        public List<string> Probes { get; } = [];
        public Task<bool> SupportsToolsAsync(string model, CancellationToken ct)
        {
            Probes.Add(model);
            return Task.FromResult(supported);
        }

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct) =>
            Task.FromResult("sync");
        public async Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
            Func<ModelDelta, Task> onDelta, CancellationToken ct)
        {
            await onDelta(new("stream"));
            return new("stream");
        }

        public async Task<ModelReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
            Func<ModelDelta, Task> onDelta, CancellationToken ct)
        {
            await onDelta(new("agent"));
            return new("agent");
        }
    }
}
