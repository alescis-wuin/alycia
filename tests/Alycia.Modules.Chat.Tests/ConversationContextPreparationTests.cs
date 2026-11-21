using Alycia.Modules.Inference;
using Alycia.Modules.Retrieval;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Memory;
using Alycia.Modules.Chat.Application.Conversations;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Context;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class ConversationContextPreparationTests
{
    [Fact]
    public async Task LeaseCoversTheTerminalWriteAndPreviewCanRunAlongsideIt()
    {
        var fixture = new Fixture();
        var service = fixture.Service();
        var id = fixture.Owner.Id;
        var revision = fixture.Owner.Revision;
        using (var pending = await service.BeginAsync(id, revision, "Question", default))
        {
            var prepared = await pending.BuildAsync(_ => Task.CompletedTask);
            Assert.Equal(revision, prepared.Revision);
            await Assert.ThrowsAsync<ChatConflictException>(() => service.BeginAsync(id, revision, "Autre", default));
            var preview = await service.PreviewAsync(id, "Aperçu", default);
            Assert.Equal("Aperçu", preview.Context.Messages[^1].Content);
        }
        using var next = await service.BeginAsync(id, revision, "Autre", default);
    }

    [Fact]
    public async Task AdmissionFailureReleasesTheLeaseAndPreservesValidationOrder()
    {
        var fixture = new Fixture();
        var service = fixture.Service();
        var id = fixture.Owner.Id;
        var revision = fixture.Owner.Revision;
        await Assert.ThrowsAsync<ChatConflictException>(() => service.BeginAsync(id, revision + 1, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.BeginAsync(id, revision, null, default));
        Assert.Equal(0, fixture.OptionReads);

        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(id, null, default));
        Assert.Equal(1, fixture.OptionReads);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BeginAsync(id, revision, "Question", cancelled.Token));
        using var next = await service.BeginAsync(id, revision, "Question", default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultsAreCapturedBeforeTheConversationLookup(bool explicitPreparation)
    {
        var fixture = new Fixture { Lookup = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        fixture.Defaults.Temperature = .19;
        var service = fixture.Service();
        async Task<ContextPreparationResult> Start()
        {
            if (!explicitPreparation) return await service.PreviewAsync(fixture.Owner.Id, "Question", default);
            using var lease = await service.BeginAsync(fixture.Owner.Id, fixture.Owner.Revision, "Question", default);
            return await lease.BuildAsync(_ => Task.CompletedTask);
        }

        var pending = Start();
        fixture.Defaults.Temperature = 1.91;
        fixture.Lookup.SetResult();
        Assert.Equal(.19, (await pending).Options.Temperature);
        Assert.Equal(1.91, (await Start()).Options.Temperature);
    }

    private sealed class Fixture : ITestConversationRepository, IChatModel, ITemporalMemory, IProjectRetrieval
    {
        public Conversation Owner { get; } = Conversation.Create("Conversation", "demo", "");
        public ChatOptions Defaults { get; } = new() { RetrievalMode = "legacy", StructuredMemory = false, UseProjectTools = false };
        public TaskCompletionSource? Lookup { get; init; }
        public int OptionReads { get; private set; }
        public ConversationContextPreparation Service() => new(this, this, new TurnTools(), new TurnContext(this, new Alycia.Modules.Context.SlidingHistory()),
            new ConversationMemory(this, ChatFixture.Gate), new TurnRouting(new QueryPlanner(this), this, this), Defaults, ChatFixture.Gate, new ModelMeterFactory());

        public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct)
        {
            OptionReads++;
            return Task.FromResult(new ModelTokenization(null));
        }
        public async Task<Conversation?> FindAsync(Guid id, CancellationToken ct)
        {
            if (Lookup is not null) await Lookup.Task.WaitAsync(ct);
            return id == Owner.Id ? Owner : null;
        }
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Attachment>>([]);
        public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(Conversation conversation, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Conversation conversation, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
        public Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => throw new NotSupportedException();
        public Task<TemporalRecall> RecallAsync(Guid owner, string question, TemporalRecallSettings settings, int through, CancellationToken ct) => throw new NotSupportedException();
        public Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ProjectSearch request, CancellationToken ct) => throw new NotSupportedException();
    }
}
