using Alycia.Contracts.Documents;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Modules.Context;

namespace Alycia.Modules.Chat.Tests;

public sealed class RecursiveContextTests
{
    [Fact]
    public async Task RecursivelyReducesOldHistoryProtectsRecentTurnsAndReusesMemory()
    {
        var repository = new Repository(30);
        var model = new Model();
        var options = new ChatSettings { ContextTokens = 4096, MaxOutputTokens = 512 };
        var prepared = await RecursiveContext.BuildAsync(repository, model, repository.Conversation,
            "Demande protégée", options.Context(), true, null, default);
        Assert.True(prepared.Changed);
        Assert.True(model.Calls > 1);
        Assert.InRange(prepared.Context.EstimatedInputTokens, 1, 4096 - 512 - 256);
        Assert.Equal("SYSTEM_PROTECTED", prepared.Context.Messages[0].Content);
        Assert.Equal(repository.Messages.TakeLast(4).Select(m => m.Content),
            prepared.Context.Messages.TakeLast(5).Take(4).Select(m => m.Content));
        Assert.Equal("Demande protégée", prepared.Context.Messages[^1].Content);
        Assert.Equal(60, repository.Messages.Count);
        Assert.InRange(prepared.Through, 2, 56);
        repository.Conversation = repository.Conversation with { MemoryText = prepared.Memory, MemoryThroughSequence = prepared.Through, MemorySourceHash = prepared.SourceHash, MemoryModel = "demo" };
        var calls = model.Calls;
        var again = await RecursiveContext.BuildAsync(repository, model, repository.Conversation,
            "Demande protégée", options.Context(), true, null, default);
        Assert.False(again.Changed);
        Assert.Equal(calls, model.Calls);
    }

    [Fact]
    public async Task RejectsAnImpossibleProtectedBudgetBeforeAnyInference()
    {
        var repository = new Repository(3);
        var model = new Model();
        await Assert.ThrowsAsync<ArgumentException>(() => RecursiveContext.BuildAsync(repository, model,
            repository.Conversation, new string('x', 3000), new ChatSettings() { ContextTokens = 2048, MaxOutputTokens = 256 }.Context(),
            true, null, default));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task AModelThatDoesNotCompressFailsWithoutReplacingMemory()
    {
        var repository = new Repository(20);
        var model = new Model { DoesNotCompress = true };
        await Assert.ThrowsAsync<ChatModelException>(() => RecursiveContext.BuildAsync(repository, model,
            repository.Conversation, "Question", new ChatSettings() { ContextTokens = 2048, MaxOutputTokens = 256 }.Context(), true, null, default));
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
            repository.Conversation, "Question", new ChatSettings() { ContextTokens = 2048, MaxOutputTokens = 256 }.Context(), true,
            _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal("", repository.Conversation.MemoryText);
        Assert.Equal(40, repository.Messages.Count);
    }

    private sealed class Model : IChatModel
    {
        public int Calls;
        public bool DoesNotCompress;
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct)
        {
            Calls++; ct.ThrowIfCancellationRequested();
            Assert.False(settings.Generation.GenerateUntilStop);
            Assert.True(messages.Sum(m => SlidingContext.EstimateTokens(m.Content))
                <= settings.Generation.ContextTokens - settings.Generation.MaxOutputTokens - settings.SafetyTokens);
            return Task.FromResult(DoesNotCompress ? new string('x', 20000) : "Décisions et contraintes conservées.");
        }
    }

    private sealed class Repository : IConversationContextReader
    {
        public ContextConversation Conversation { get; set; }
        public List<HistoryMessage> Messages { get; } = [];

        public Repository(int turns)
        {
            var id = Guid.NewGuid();
            Conversation = new(id, turns * 2, "demo", "SYSTEM_PROTECTED", "", 0, "", "", "{}");
            for (var i = 0; i < turns; i++)
            {
                var request = Guid.NewGuid();
                Messages.Add(new(id, i * 2 + 1, request, "user", $"Question {i} " + new string('u', 180), DateTimeOffset.UnixEpoch));
                Messages.Add(new(id, i * 2 + 2, request, "assistant", $"Réponse {i} " + new string('a', 180), DateTimeOffset.UnixEpoch));
            }
        }

        public Task<IReadOnlyList<HistoryMessage>> RecentAsync(Guid id, int limit, int? before, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HistoryMessage>>(Messages.Where(m => m.ConversationId == id && (before == null || m.Sequence < before)).TakeLast(limit).ToArray());
        public Task<IReadOnlyList<HistoryMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HistoryMessage>>(Messages.Where(m => m.ConversationId == id && m.Sequence > after && m.Sequence <= through).ToArray());
        public Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TurnFile>>([]);
    }
}
