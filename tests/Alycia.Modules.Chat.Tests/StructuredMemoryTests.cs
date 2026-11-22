using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class StructuredMemoryTests
{
    [Fact]
    public void PreparedMemoryRetainsItsFormatAndWaitsForTheCallersCommit()
    {
        var repo = new Repository(1);
        var conversation = repo.Conversation;
        var memory = new ConversationMemory(repo, ChatFixture.Gate);
        var prepared = new PreparedContext(new([], 0, 0), "Résumé du premier tour", 2, "source", true);
        var revision = conversation.Revision;
        Assert.False(memory.Apply(conversation, null));
        Assert.False(memory.Apply(conversation, (prepared with { Changed = false }).MemoryChange()));
        Assert.Equal(revision, conversation.Revision);

        Assert.True(memory.Apply(conversation, prepared.MemoryChange()));
        Assert.Equal(("Résumé du premier tour", 2, "source", "demo"),
            (conversation.MemoryText, conversation.MemoryThroughSequence, conversation.MemorySourceHash, conversation.MemoryModel));
        Assert.Equal("{}", conversation.MemoryLedgerJson);
        var ledger = StructuredMemory.Serialize(new(1, 2, "chain", []));
        Assert.True(memory.Apply(conversation, (prepared with { LedgerJson = ledger, Memory = "ignoré" }).MemoryChange()));
        Assert.Equal(ledger, conversation.MemoryLedgerJson);
        Assert.Equal("Résumé du premier tour", conversation.MemoryText);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task ResetChecksTheGateAndRevisionBeforeClearingBothMemoryFormatsTogether()
    {
        var repo = new Repository(1);
        var conversation = repo.Conversation;
        conversation.SetMemory("résumé", 2, "source", "demo");
        var ledger = StructuredMemory.Serialize(new(1, 2, "chain", []));
        conversation.SetMemoryLedger(ledger);
        var revision = conversation.Revision;
        var memory = new ConversationMemory(repo, ChatFixture.Gate);
        using (ChatFixture.Gate.Enter(conversation.Id))
            await Assert.ThrowsAsync<ChatConflictException>(() => memory.ResetAsync(conversation.Id, revision, default));
        await Assert.ThrowsAsync<ChatConflictException>(() => memory.ResetAsync(conversation.Id, revision - 1, default));
        Assert.Equal((revision, "résumé", ledger, 0), (conversation.Revision, conversation.MemoryText, conversation.MemoryLedgerJson, repo.Saves));

        var reset = await memory.ResetAsync(conversation.Id, revision, default);
        Assert.Equal(revision + 2, reset.Revision);
        Assert.Equal(("", 0, "", "", "{}"),
            (reset.MemoryText, reset.MemoryThroughSequence, reset.MemorySourceHash, reset.MemoryModel, reset.MemoryLedgerJson));
        Assert.Equal(2, reset.MessageCount);
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public void TemporalProjectionRetainsOnlySourceBackedLedgerEvidence()
    {
        var source = new ChatMessage(Guid.NewGuid(), 2, Guid.NewGuid(), "assistant", "Lieu envisagé : Normandie.");
        var original = new MemoryEntry("Région", "fact", "Normandie", 2, "assistant", StructuredMemory.Hash(source.Content), Correction: true);
        var facts = TemporalMemory.IndexFacts(source.ToTemporal(),
        [
            original, original, original with { Key = "signature", SourceHash = "autre" }, original with { Key = "inversé", Role = "user" },
            original with { Key = "séquence", Sequence = 4 }, original with { Key = "inventé", Quote = "Bretagne" },
            original with { Key = "secours", Fallback = true }
        ]);
        Assert.Equal(2, facts.Length);
        var confirmed = Assert.Single(facts, f => f.Key == StructuredMemory.CanonicalKey(original.Key));
        Assert.Equal(original.Quote, source.Content.Substring(confirmed.Offset, confirmed.Quote.Length));
        Assert.Equal((source.Role, source.CreatedAtUtc, source.Sequence, source.Content.Length, original.SourceHash, false, "proposition"),
            (confirmed.Role, confirmed.Observed, confirmed.Sequence, confirmed.FullLength, confirmed.Sha256, confirmed.Correction, confirmed.Kind));
    }

    [Fact]
    public void RejectsFabricatedQuotesAndKeepsContradictionsUntilUserCorrection()
    {
        var id = Guid.NewGuid();
        var source = new ChatMessage(id, 1, Guid.NewGuid(), "user", "La base est SQLite.");
        var entries = StructuredMemory.Validate("""
            {"entries":[{"key":"base","kind":"fact","sequence":1,"quote":"La base est SQLite."},
            {"key":"base","kind":"fact","sequence":1,"quote":"La base est PostgreSQL."}]}
            """, [(source.ToHistory(), source.Content)]);
        Assert.Single(entries);
        Assert.Equal(StructuredMemory.Hash(source.Content), entries[0].SourceHash);
        var other = new MemoryEntry("base", "fact", "La base est PostgreSQL.", 3, "user", "hash");
        var ledger = new MemoryLedger(1, 4, "hash", [entries[0], other]);
        var selected = StructuredMemory.Select(ledger, "base", [], 2000);
        Assert.Equal(1, selected.Report.Conflicts);
        Assert.Contains("SQLite", selected.Text); Assert.Contains("PostgreSQL", selected.Text);
        Assert.Equal(0, StructuredMemory.Select(ledger, "base", [], 100).Report.Selected);
        var assistant = new ChatMessage(id, 6, Guid.NewGuid(), "assistant", "Correction [base]: MySQL");
        Assert.Equal(1, StructuredMemory.Select(ledger, "base", [assistant.ToHistory()], 2000).Report.Conflicts);
        var corrected = StructuredMemory.Select(ledger, "Correction [base]: MariaDB", [], 2000);
        Assert.Equal(0, corrected.Report.Selected);
        Assert.DoesNotContain("SQLite", corrected.Text);
    }

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
