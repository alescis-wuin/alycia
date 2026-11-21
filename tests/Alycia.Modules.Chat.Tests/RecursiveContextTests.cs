using Alycia.Contracts.Documents;
using Alycia.Modules.Inference;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Context;
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
        var prepared = await RecursiveContext.BuildAsync(repository, model, repository.Conversation.ForContext(options),
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
        repository.Conversation.SetMemory(prepared.Memory, prepared.Through, prepared.SourceHash, "demo");
        var calls = model.Calls;
        var again = await RecursiveContext.BuildAsync(repository, model, repository.Conversation.ForContext(options),
            "Demande protégée", options.Context(), true, null, default);
        Assert.False(again.Changed);
        Assert.Equal(calls, model.Calls);
    }

    [Fact]
    public async Task PreviewNeverInvokesTheModelAndSignalsIncompletePreparation()
    {
        var repository = new Repository(20);
        var model = new Model();
        var meter = new MeteredChatModel(model);
        var turn = new TurnContext(repository, new Alycia.Modules.Context.SlidingHistory());
        var options = new ChatOptions { ContextTokens = 2048, MaxOutputTokens = 256, StructuredMemory = false };
        var budget = await turn.AllocateAsync(repository.Conversation.ForContext(options), "Question", options.Context(), false, default);
        var result = await turn.BuildAsync(repository.Conversation.ForContext(options), "Question", budget, model, meter,
            TurnContextMode.Preview, _ => throw new InvalidOperationException("Unexpected progress in preview"), default);
        Assert.True(result.Window.NeedsSummary);
        Assert.Empty(meter.Snapshot(options.ModelIdentity, options.TokenCounter).Calls);
        Assert.Equal(0, model.Calls);
        Assert.Equal("", repository.Conversation.MemoryText);
    }

    [Theory]
    [InlineData(TurnContextMode.Send)]
    [InlineData(TurnContextMode.PrepareMemory)]
    public async Task ReturnsPendingSummariesAndMetersTheirCalls(TurnContextMode mode)
    {
        var repository = new Repository(20);
        var owner = repository.Conversation;
        var revision = owner.Revision;
        var options = new ChatOptions { AutoSummarize = mode == TurnContextMode.Send, StructuredMemory = false, ContextTokens = 4096, MaxOutputTokens = 512 };
        var model = new Model();
        var meter = new MeteredChatModel(model);
        var turn = new TurnContext(repository, new Alycia.Modules.Context.SlidingHistory());
        var budget = await turn.AllocateAsync(owner.ForContext(options), "Question", options.Context(), false, default);
        var progress = new List<string>();
        var result = await turn.BuildAsync(owner.ForContext(options), "Question", budget, model, meter, mode,
            status => { progress.Add(status); return Task.CompletedTask; }, default);

        Assert.True(result.Prepared!.Changed);
        Assert.Equal(result.Prepared.Memory, result.Window.SummaryText);
        Assert.NotEmpty(result.Prepared.SourceHash);
        Assert.Equal(model.Calls, meter.Snapshot(options.ModelIdentity, options.TokenCounter).Calls.Count);
        Assert.NotEmpty(progress);
        Assert.Equal(0, owner.MemoryThroughSequence);
        Assert.Equal("", owner.MemoryText);
        Assert.Equal(revision, owner.Revision);
    }

    [Theory]
    [InlineData(TurnContextMode.Preview)]
    [InlineData(TurnContextMode.Send)]
    public async Task SlidingWindowKeepsRecentTurnEligibleGuideAndToolReserve(TurnContextMode mode)
    {
        var repository = new Repository(4);
        var owner = repository.Conversation;
        var file = new Attachment { Id = Guid.NewGuid(), Name = "actuel.md", Text = "Passage original", AnchorSequence = 7, Sha256 = "sha" };
        repository.Files.Add(file);
        repository.Files.Add(new() { Id = Guid.NewGuid(), Name = "ancien.md", Text = "Hors fenêtre", AnchorSequence = 6 });
        var passage = new SourceChunk(file.Id, file.Name, file.Sha256, "section", "text", 0, file.Text.Length, 1, 1, file.Text, [], true);
        var guide = new PassageGuide(ProjectRetrieval.Reference(passage), "Synthèse en cache", "", "fixture", "cached", null, null, null, DateTimeOffset.UnixEpoch, "node", "passage");
        var options = new ChatOptions
        {
            AutoSummarize = false,
            MaxHistoryTurns = 1,
            ContextTokens = 32768,
            SafetyTokens = 256,
            SourceCandidates = [passage with { Guide = guide }],
            QuerySummaryTokens = 512,
            Route = new("retrieval", "fixture", true, false)
        };
        var model = new Model();
        var turn = new TurnContext(repository, new Alycia.Modules.Context.SlidingHistory());
        var budget = await turn.AllocateAsync(owner.ForContext(options), "Question", options.Context(), true, default);
        var result = await turn.BuildAsync(owner.ForContext(options), "Question", budget, model, new MeteredChatModel(model), mode, null, default);

        Assert.Null(result.Prepared);
        Assert.Equal(repository.Messages.TakeLast(2).Select(m => m.Content), result.Window.Messages.Skip(1).Take(2).Select(m => m.Content));
        Assert.Equal(1, result.Window.FilesAvailable);
        Assert.Equal(file.Id, Assert.Single(result.Window.Sources!).Id);
        Assert.Equal(guide.Reference, Assert.Single(result.Window.Guides!).Reference);
        Assert.Contains(guide.Summary, result.Window.Messages[^1].Content);
        Assert.Equal(0, model.Calls);
        Assert.True(budget.ToolReserve > 0);
        Assert.Equal(256, options.SafetyTokens);
        Assert.Equal(options.SafetyTokens + result.Window.ToolReserve, budget.Options.SafetyTokens);
        Assert.Equal(mode == TurnContextMode.Preview ? options.Route : null, result.Window.Route);
    }

    [Theory]
    [InlineData(TurnContextMode.Preview)]
    [InlineData(TurnContextMode.Send)]
    public async Task TemporalRecallRetainsItsSourceOutsideTheSlidingWindowWhenSummariesAreDisabled(TurnContextMode mode)
    {
        var repository = new Repository(4);
        var fact = Assert.Single(TemporalMemory.Extract(repository.Messages[0].ToTemporal()));
        var options = new ChatOptions { AutoSummarize = false, MaxHistoryTurns = 1, Recall = new([fact], 1, 1, []) };
        var turn = new TurnContext(repository, new Alycia.Modules.Context.SlidingHistory());
        var budget = await turn.AllocateAsync(repository.Conversation.ForContext(options), "Question", options.Context(), false, default);
        var model = new Model();
        var result = await turn.BuildAsync(repository.Conversation.ForContext(options), "Question", budget, model, new MeteredChatModel(model), mode, null, default);

        Assert.Equal(fact.Sequence, Assert.Single(result.Window.Temporal!.Sources).Sequence);
        Assert.Contains(result.Window.Messages, m => m.Content.Contains(fact.Quote));
        Assert.False(result.Prepared!.Changed);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task RejectsAnImpossibleProtectedBudgetBeforeAnyInference()
    {
        var repository = new Repository(3);
        var model = new Model();
        await Assert.ThrowsAsync<ArgumentException>(() => RecursiveContext.BuildAsync(repository, model,
            repository.Conversation.ForContext(new()), new string('x', 3000), new ChatOptions() { ContextTokens = 2048, MaxOutputTokens = 256 }.Context(),
            true, null, default));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task AModelThatDoesNotCompressFailsWithoutReplacingMemory()
    {
        var repository = new Repository(20);
        var model = new Model { DoesNotCompress = true };
        await Assert.ThrowsAsync<ChatModelException>(() => RecursiveContext.BuildAsync(repository, model,
            repository.Conversation.ForContext(new()), "Question", new ChatOptions() { ContextTokens = 2048, MaxOutputTokens = 256 }.Context(), true, null, default));
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
            repository.Conversation.ForContext(new()), "Question", new ChatOptions() { ContextTokens = 2048, MaxOutputTokens = 256 }.Context(), true,
            _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal("", repository.Conversation.MemoryText);
        Assert.Equal(40, repository.Messages.Count);
    }

    [Fact]
    public void UnicodeChunkingKeepsEveryCharacterAndItsByteBound()
    {
        var text = string.Concat(Enumerable.Repeat("é🙂漢字\n", 100));
        var chunks = Utf8Text.Chunks(text, 37).ToArray();
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
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, CancellationToken ct)
        {
            Calls++; ct.ThrowIfCancellationRequested();
            Assert.False(settings.Generation.GenerateUntilStop);
            Assert.True(messages.Sum(m => SlidingContext.EstimateTokens(m.Content))
                <= settings.Generation.ContextTokens - settings.Generation.MaxOutputTokens - settings.SafetyTokens);
            return Task.FromResult(DoesNotCompress ? new string('x', 20000) : "Décisions et contraintes conservées.");
        }
    }

    private sealed class Repository : ITestConversationRepository
    {
        public Conversation Conversation { get; } = Conversation.Create("test", "demo", "SYSTEM_PROTECTED");
        public List<ChatMessage> Messages { get; } = [];
        public List<Attachment> Files { get; } = [];
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
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Attachment>>(Files);
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
