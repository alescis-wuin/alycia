using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class RecursiveContextTests
{
    [Fact]
    public async Task RecursivelyReducesOldHistoryProtectsRecentTurnsAndReusesMemory()
    {
        var repository = new Repository(30);
        var model = new Model();
        var options = new ChatOptions { ContextTokens = 4096, MaxOutputTokens = 512 };
        var prepared = await RecursiveContext.BuildAsync(repository, model, repository.Conversation,
            "Demande protégée", options, true, null, default);
        Assert.True(prepared.Changed);
        Assert.True(model.Calls > 1);
        Assert.InRange(prepared.Context.EstimatedInputTokens, 1, 4096 - 512 - 256);
        Assert.Equal("SYSTEM_PROTECTED", prepared.Context.Messages[0].Content);
        Assert.Equal(repository.Messages.TakeLast(4).Select(m => m.Content),
            prepared.Context.Messages.TakeLast(5).Take(4).Select(m => m.Content));
        Assert.Equal("Demande protégée", prepared.Context.Messages[^1].Content);
        Assert.Equal(60, repository.Messages.Count);
        Assert.InRange(prepared.Through, 2, 56);
        repository.Conversation.SetMemory(prepared.Memory, prepared.Through, prepared.SourceHash, "demo");
        var calls = model.Calls;
        var again = await RecursiveContext.BuildAsync(repository, model, repository.Conversation,
            "Demande protégée", options, true, null, default);
        Assert.False(again.Changed);
        Assert.Equal(calls, model.Calls);
    }

    [Fact]
    public async Task PreviewNeverInvokesTheModelAndSignalsIncompletePreparation()
    {
        var repository = new Repository(20);
        var model = new Model();
        var prepared = await RecursiveContext.BuildAsync(repository, model, repository.Conversation,
            "Question", new() { ContextTokens = 2048, MaxOutputTokens = 256 }, false, null, default);
        Assert.True(prepared.Context.NeedsSummary);
        Assert.Equal(0, model.Calls);
        Assert.Equal("", repository.Conversation.MemoryText);
    }

    [Fact]
    public async Task RejectsAnImpossibleProtectedBudgetBeforeAnyInference()
    {
        var repository = new Repository(3);
        var model = new Model();
        await Assert.ThrowsAsync<ArgumentException>(() => RecursiveContext.BuildAsync(repository, model,
            repository.Conversation, new string('x', 3000), new() { ContextTokens = 2048, MaxOutputTokens = 256 },
            true, null, default));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task AModelThatDoesNotCompressFailsWithoutReplacingMemory()
    {
        var repository = new Repository(20);
        var model = new Model { DoesNotCompress = true };
        await Assert.ThrowsAsync<ChatModelException>(() => RecursiveContext.BuildAsync(repository, model,
            repository.Conversation, "Question", new() { ContextTokens = 2048, MaxOutputTokens = 256 }, true, null, default));
        Assert.Equal("", repository.Conversation.MemoryText);
        Assert.Equal(40, repository.Messages.Count);
    }

    [Fact]
    public async Task CancellingReductionDoesNotReplaceTheOriginalHistoryOrMemory()
    {
        var repository = new Repository(20);
        var model = new Model();
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecursiveContext.BuildAsync(repository, model,
            repository.Conversation, "Question", new() { ContextTokens = 2048, MaxOutputTokens = 256 }, true,
            _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal("", repository.Conversation.MemoryText);
        Assert.Equal(40, repository.Messages.Count);
    }

    [Fact]
    public void UnicodeChunkingKeepsEveryCharacterAndItsByteBound()
    {
        var text = string.Concat(Enumerable.Repeat("é🙂漢字\n", 100));
        var chunks = RecursiveSummarizer.Chunks(text, 37).ToArray();
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(c), 1, 37));
    }

    private sealed class Model : IChatModel
    {
        public int Calls;
        public bool DoesNotCompress;
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings, CancellationToken ct)
        {
            Calls++; ct.ThrowIfCancellationRequested();
            Assert.False(settings.GenerateUntilStop);
            Assert.True(messages.Sum(m => SlidingContext.EstimateTokens(m.Content))
                <= settings.ContextTokens - settings.MaxOutputTokens - settings.SafetyTokens);
            return Task.FromResult(DoesNotCompress ? new string('x', 20000) : "Décisions et contraintes conservées.");
        }
    }

    private sealed class Repository : IChatRepository
    {
        public Conversation Conversation { get; } = Conversation.Create("test", "demo", "SYSTEM_PROTECTED");
        public List<ChatMessage> Messages { get; } = [];
        public Repository(int turns)
        {
            for (var i = 0; i < turns; i++)
            {
                var request = Guid.NewGuid();
                Messages.Add(new(Conversation.Id, i * 2 + 1, request, "user", $"Question {i} " + new string('u', 180)));
                Messages.Add(new(Conversation.Id, i * 2 + 2, request, "assistant", $"Réponse {i} " + new string('a', 180)));
                Conversation.CompleteTurn();
            }
        }
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Attachment>>([]);
        public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(Conversation);
        public Task AddAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => before == null || m.Sequence < before).TakeLast(limit).ToArray());
        public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid request, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task AppendTurnAsync(Conversation c, ChatMessage u, ChatMessage a, CancellationToken ct) => Task.CompletedTask;
    }
}
