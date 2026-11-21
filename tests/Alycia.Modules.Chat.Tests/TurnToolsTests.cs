using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Contracts.Tools;

namespace Alycia.Modules.Chat.Tests;

public sealed class TurnToolsTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task SendSelectsSnapshotOnlyForAttachedConversationWithSnapshotProvider(bool attached, bool hasSnapshot)
    {
        var scope = new ToolTurnScope(Guid.NewGuid(), attached ? Guid.NewGuid() : null, "demo");
        var ordinary = new Sources(true);
        var snapshot = hasSnapshot ? new Sources(true) : null;
        var model = new Model(true);

        var selected = await new TurnTools(ordinary, snapshot).SelectAsync(scope, true, model, default);

        var expected = attached && hasSnapshot ? snapshot! : ordinary;
        Assert.Same(expected, selected);
        Assert.Equal([scope.ConversationId], expected.Probes);
        if (!ReferenceEquals(ordinary, expected)) Assert.Empty(ordinary.Probes);
        if (snapshot is not null && !ReferenceEquals(snapshot, expected)) Assert.Empty(snapshot.Probes);
        Assert.Equal([scope.Model], model.Probes);
    }

    [Theory]
    [InlineData(false, true, true, 0, 0)]
    [InlineData(true, false, true, 1, 0)]
    [InlineData(true, true, false, 1, 1)]
    public async Task RefusalStopsAtFirstFailedConditionWithoutTryingOtherProvider(bool enabled, bool hasSources,
        bool supported, int expectedSourceProbes, int expectedModelProbes)
    {
        var scope = new ToolTurnScope(Guid.NewGuid(), Guid.NewGuid(), "demo");
        var ordinary = new Sources(true);
        var snapshot = new Sources(hasSources);
        var model = new Model(supported);

        var selected = await new TurnTools(ordinary, snapshot).SelectAsync(scope, enabled, model, default);

        Assert.Null(selected);
        Assert.Equal(expectedSourceProbes, snapshot.Probes.Count);
        Assert.Equal(expectedModelProbes, model.Probes.Count);
        Assert.Empty(ordinary.Probes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextChecksOnlyOrdinarySourcesEvenWhenAttached(bool available)
    {
        var scope = new ToolTurnScope(Guid.NewGuid(), Guid.NewGuid(), "demo");
        var ordinary = new Sources(available);
        var snapshot = new Sources(!available);

        var result = await new TurnTools(ordinary, snapshot).AvailableForContextAsync(scope, true, new Model(true), default);

        Assert.Equal(available, result);
        Assert.Equal([scope.ConversationId], ordinary.Probes);
        Assert.Empty(snapshot.Probes);
    }

    [Fact]
    public async Task CancellationAtSourceCheckDoesNotQueryModel()
    {
        var scope = new ToolTurnScope(Guid.NewGuid(), null, "demo");
        var model = new Model(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TurnTools(new Sources(true)).SelectAsync(scope, true, model, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(model.Probes);
    }

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

        var reply = await new TurnTools().GenerateAsync(new Model(true), active ? new Sources(true) : null,
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
