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

    private sealed class Fixture : ITestConversationRepository, IChatModel, ITemporalMemory, IProjectRetrieval
    {
        public Conversation Owner { get; } = Conversation.Create("Conversation", "demo", "");
        public int OptionReads { get; private set; }
        public ConversationContextPreparation Service() => new(this, this, new TurnTools(), new TurnContext(this, new Alycia.Modules.Context.SlidingHistory()),
            new ConversationMemory(this, ChatFixture.Gate), this, this, new() { RetrievalMode = "legacy", StructuredMemory = false, UseProjectTools = false }, ChatFixture.Gate);

        public Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions options, CancellationToken ct)
        {
            OptionReads++;
            return Task.FromResult(options.Copy());
        }
        public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(id == Owner.Id ? Owner : null);
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
        public Task<TemporalRecall> RecallAsync(Guid owner, string question, ChatOptions settings, int through, CancellationToken ct) => throw new NotSupportedException();
        public Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ChatOptions settings, CancellationToken ct) => throw new NotSupportedException();
    }
}
