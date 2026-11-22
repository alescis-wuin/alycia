using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class StructuredMemoryTests
{
    [Fact]
    public async Task ExtractsOnlyUnderPressurePreservesProtectedBlockAndReusesQuotedMemory()
    {
        var repo = new Repository(12); var model = new Model();
        var options = new ChatOptions { ContextTokens = 4096, MaxOutputTokens = 512 };
        var preview = await ConversationContext.BuildAsync(new ContextMemory(), repo, model, repo.Conversation.ForContext(options), "budget", options.Context(), false, null, default);
        Assert.True(preview.Context.NeedsSummary); Assert.Equal(0, model.Calls);
        var result = await ConversationContext.BuildAsync(new ContextMemory(), repo, model, repo.Conversation.ForContext(options), "budget", options.Context(), true, null, default);
        Assert.True(result.Changed); Assert.NotNull(result.LedgerJson);
        Assert.Equal(20, result.Through);
        Assert.InRange(result.Context.EstimatedInputTokens, 1, 4096 - 512 - 256);
        Assert.Equal("SYSTEM", result.Context.Messages[0].Content);
        Assert.Equal(repo.Messages.TakeLast(4).Select(m => m.Content), result.Context.Messages.TakeLast(5).Take(4).Select(m => m.Content));
        Assert.Equal("budget", result.Context.Messages[^1].Content);
        var ledger = StructuredMemory.Read(result.LedgerJson!);
        Assert.All(ledger.Entries, e => Assert.Contains(e.Quote, repo.Messages.Single(m => m.Sequence == e.Sequence).Content));
        repo.Conversation.SetMemoryLedger(result.LedgerJson!);
        var calls = model.Calls;
        var again = await ConversationContext.BuildAsync(new ContextMemory(), repo, model, repo.Conversation.ForContext(options), "budget", options.Context(), true, null, default);
        Assert.False(again.Changed); Assert.Equal(calls, model.Calls);
        Assert.Equal(24, repo.Messages.Count);
        var changedModel = options.Copy(); changedModel.ModelIdentity = "demo:new-digest";
        var rebuilt = await ConversationContext.BuildAsync(new ContextMemory(), repo, model, repo.Conversation.ForContext(changedModel), "budget", changedModel.Context(), true, null, default);
        Assert.True(rebuilt.Changed); Assert.True(model.Calls > calls);
        Assert.Equal("demo:new-digest", StructuredMemory.Read(rebuilt.LedgerJson!).ModelIdentity);
        Assert.Equal(24, repo.Messages.Count);
    }

    [Fact]
    public async Task InvalidExtractionUsesLabelledOriginalExcerptsAndFailedTurnDoesNotSaveLedger()
    {
        var repo = new Repository(12); var model = new Model { Malformed = true };
        var options = new ChatOptions { ContextTokens = 4096, MaxOutputTokens = 512 };
        var result = await ConversationContext.BuildAsync(new ContextMemory(), repo, model, repo.Conversation.ForContext(options), "budget", options.Context(), true, null, default);
        Assert.True(result.Context.Memory!.FallbackSegments > 0);
        Assert.All(StructuredMemory.Read(result.LedgerJson!).Entries, e => Assert.True(e.Fallback));
        Assert.Equal("{}", repo.Conversation.MemoryLedgerJson);
        model.FailAnswer = true;
        await Assert.ThrowsAsync<ChatModelException>(() => ChatFixture.Create(repo, model, options).SendAsync(repo.Conversation.Id, Guid.NewGuid(), "budget", default));
        Assert.Equal("{}", repo.Conversation.MemoryLedgerJson); Assert.Equal(24, repo.Messages.Count);
    }

    [Fact]
    public async Task SmallHistoryNeedsNoExtractionAndCancellationNeverPersistsPartialMemory()
    {
        var model = new Model(); var small = new Repository(3);
        var result = await ConversationContext.BuildAsync(new ContextMemory(), small, model, small.Conversation.ForContext(new()), "question", new ChatOptions().Context(), true, null, default);
        Assert.Equal(6, result.Context.HistoryMessagesIncluded); Assert.Equal(0, model.Calls);
        var large = new Repository(12); using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConversationContext.BuildAsync(new ContextMemory(), large, model, large.Conversation.ForContext(new()),
            "budget", new ChatOptions() { ContextTokens = 4096, MaxOutputTokens = 512 }.Context(), true, _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal("{}", large.Conversation.MemoryLedgerJson);
    }

    private sealed class Model : IChatModel
    {
        public int Calls; public bool Malformed; public bool FailAnswer;
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall options, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (options.Generation.CallPurpose != "memory") return FailAnswer ? Task.FromException<string>(new ChatModelException("fail")) : Task.FromResult("réponse");
            Calls++; Assert.NotNull(options.Generation.ResponseSchema); Assert.False(options.Generation.GenerateUntilStop);
            Assert.True(messages.Sum(m => PromptTokens.Count(m.Content, options.TokenCounter)) <= options.Generation.ContextTokens - options.Generation.MaxOutputTokens - options.SafetyTokens);
            if (Malformed) return Task.FromResult("pas du JSON");
            using var input = JsonDocument.Parse(messages[^1].Content);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                entries = input.RootElement.GetProperty("messages").EnumerateArray().Select(m => new
                {
                    key = "budget",
                    kind = "constraint",
                    sequence = m.GetProperty("sequence").GetInt32(),
                    quote = m.GetProperty("content").GetString()![..20]
                })
            }));
        }
    }
    private sealed class Repository : ITestConversationRepository
    {
        public Conversation Conversation { get; } = Conversation.Create("test", "demo", "SYSTEM");
        public List<ChatMessage> Messages { get; } = [];
        public int Saves { get; private set; }
        public Repository(int turns)
        {
            for (var i = 0; i < turns; i++)
            {
                var request = Guid.NewGuid();
                Messages.Add(new(Conversation.Id, i * 2 + 1, request, "user", $"Budget demandé {i:00} : " + new string('u', 240)));
                Messages.Add(new(Conversation.Id, i * 2 + 2, request, "assistant", $"Budget proposé {i:00} : " + new string('a', 240)));
                Conversation.CompleteTurn();
            }
        }
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Attachment>>([]);
        public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(Conversation);
        public Task AddAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => before == null || m.Sequence < before).TakeLast(limit).ToArray());
        public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid request, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task AppendTurnAsync(Conversation c, ChatMessage u, ChatMessage a, CancellationToken ct) { Messages.Add(u); Messages.Add(a); return Task.CompletedTask; }
    }
}
