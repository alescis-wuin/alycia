using Alycia.Contracts.Chat;
using Alycia.Contracts.Tools;
using Alycia.Modules.Testing;
using Alycia.Modules.Tools;
using System.Text.Json;

namespace Alycia.Modules.Chat.Tests;

public sealed class TurnToolsTests
{
    [Fact]
    public async Task Tool_selection_binds_snapshot_and_ordinary_access_to_the_current_owner()
    {
        foreach (var simulated in new[] { false, true })
        {
            var scope = new ToolTurnScope(Guid.NewGuid(), Guid.NewGuid(), "demo");
            var ordinary = new Sources(false);
            var snapshot = new Sources(true);
            ITurnTools adapter = simulated ? new FixtureTurnTools(ordinary, snapshot) : new TurnTools((_, _, _, _, _, _, _) => throw new InvalidOperationException("Unexpected summary during tool selection"), ordinary, snapshot);
            Assert.Same(snapshot, await adapter.SelectAsync(scope, true, new Model(true), default));
            Assert.False(await adapter.AvailableForContextAsync(scope, true, new Model(true), default));
            Assert.Null(await adapter.SelectAsync(scope with { WorkspaceId = null }, true, new Model(true), default));
            Assert.Null(await adapter.SelectAsync(scope, true, new Model(false), default));
            Assert.All(snapshot.Probes.Concat(ordinary.Probes), owner => Assert.Equal(scope.ConversationId, owner));
        }
    }

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

        var selected = await new TurnTools((_, _, _, _, _, _, _) => throw new InvalidOperationException("Unexpected summary during tool selection"), ordinary, snapshot).SelectAsync(scope, true, model, default);

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

        var selected = await new TurnTools((_, _, _, _, _, _, _) => throw new InvalidOperationException("Unexpected summary during tool selection"), ordinary, snapshot).SelectAsync(scope, enabled, model, default);

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

        var result = await new TurnTools((_, _, _, _, _, _, _) => throw new InvalidOperationException("Unexpected summary during tool selection"), ordinary, snapshot).AvailableForContextAsync(scope, true, new Model(true), default);

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
            new TurnTools((_, _, _, _, _, _, _) => throw new InvalidOperationException("Unexpected summary during tool selection"), new Sources(true)).SelectAsync(scope, true, model, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(model.Probes);
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
