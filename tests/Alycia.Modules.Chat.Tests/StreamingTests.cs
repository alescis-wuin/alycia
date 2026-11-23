using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task PartialFailureNeverCommitsAndRetryRemainsIdempotent()
    {
        var repository = new MemoryRepository();
        var model = new StreamingModel { Fail = true };
        var service = ChatFixture.Create(repository, model, new());
        var id = repository.Conversation.Id;
        var request = Guid.NewGuid();
        var received = new List<ModelDelta>();
        await Assert.ThrowsAsync<ChatModelException>(() => service.StreamAsync(id, request, "hello", 0,
            delta => { received.Add(delta); return Task.CompletedTask; }, default));
        Assert.NotEmpty(received);
        Assert.Empty(repository.Messages);
        Assert.Equal(0, repository.Conversation.MessageCount);
        model.Fail = false;
        var result = await service.StreamAsync(id, request, "hello", 0, _ => Task.CompletedTask, default);
        Assert.Equal(2, result.Messages.Count);
        var replay = await service.StreamAsync(id, request, "hello", 0,
            _ => throw new Exception("Replayed generation must not run"), default);
        Assert.True(replay.Replayed);
        Assert.Equal(2, repository.Messages.Count);
    }

    [Fact]
    public async Task CancellationAfterDeltaDoesNotPersistAndReleasesGate()
    {
        var repository = new MemoryRepository();
        var service = ChatFixture.Create(repository, new StreamingModel(), new());
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StreamAsync(repository.Conversation.Id,
            Guid.NewGuid(), "hello", 0, _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Empty(repository.Messages);
        using var lease = ChatFixture.Gate.Enter(repository.Conversation.Id);
        Assert.Throws<ChatConflictException>(() => ChatFixture.Gate.Enter(repository.Conversation.Id));
    }

    private sealed class StreamingModel : IChatModel
    {
        public bool Fail { get; set; }
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => Task.FromResult("reply");
        public async Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
            Func<ModelDelta, Task> onDelta, CancellationToken ct)
        {
            await onDelta(new("partial"));
            if (Fail) throw new ChatModelException("Interrupted");
            ct.ThrowIfCancellationRequested();
            return new("reply");
        }
    }

    private sealed class MemoryRepository : ITestConversationRepository
    {
        public Conversation Conversation { get; } = Conversation.Create("test", "demo", "");
        public List<ChatMessage> Messages { get; } = [];
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Attachment>>([]);
        public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(Conversation);
        public Task AddAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages);
        public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.RequestId == requestId).ToArray());
        public Task AppendTurnAsync(Conversation c, ChatMessage user, ChatMessage assistant, CancellationToken ct)
        { Messages.AddRange([user, assistant]); return Task.CompletedTask; }
    }
}
