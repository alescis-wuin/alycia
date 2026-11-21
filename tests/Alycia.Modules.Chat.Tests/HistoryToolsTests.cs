using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class HistoryToolsTests
{
    [Fact]
    public async Task OldDecisionRemainsSearchableBeyondLedgerLimitAndKeepsBothRoles()
    {
        var repository = new Repository(300); var tools = new HistoryTools(repository);
        var result = await tools.CallAsync(repository.Conversation.Id, "history_search", JsonSerializer.SerializeToElement(new { query = "orion" }), 8192, default);
        Assert.Contains("/srv/orion", result.Text);
        Assert.Contains(result.History!, h => h.Sequence == 1 && h.Role == "user");
        Assert.Contains(result.History!, h => h.Sequence == 2 && h.Role == "assistant");
        Assert.Equal(600, repository.Messages.Count);
        var read = await tools.CallAsync(repository.Conversation.Id, "history_read", JsonSerializer.SerializeToElement(new { sequence = 2 }), 2048, default);
        Assert.Equal(2, read.History!.Count); Assert.InRange(Encoding.UTF8.GetByteCount(read.Text), 1, 2048);
        var citation = read.History[0].Reference;
        var audit = CitationAudit.Check("Décision initiale [" + citation + "]", [], [new(1, "history_read", "{}", true, read.Text.Length, History: read.History)]);
        Assert.Equal(new[] { citation }, audit.Recognized); Assert.Empty(audit.Unknown);
        Assert.Contains("assistant_proposal", read.Text);
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => tools.CallAsync(Guid.NewGuid(), "history_read", JsonSerializer.SerializeToElement(new { sequence = 1 }), 2048, default));
    }

    [Fact]
    public async Task NoMatchReturnsScanContinuationAndInvalidOffsetsNeverReadAnotherTurn()
    {
        var repository = new Repository(2100); var tools = new HistoryTools(repository);
        var result = await tools.CallAsync(repository.Conversation.Id, "history_search", JsonSerializer.SerializeToElement(new { query = "neptune" }), 2048, default);
        using var json = JsonDocument.Parse(result.Text);
        Assert.Equal(HistoryTools.MaxScanMessages, json.RootElement.GetProperty("scannedMessages").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, json.RootElement.GetProperty("nextBefore").ValueKind);
        Assert.Equal(0, json.RootElement.GetProperty("matches").GetArrayLength());
        await Assert.ThrowsAsync<ArgumentException>(() => tools.CallAsync(repository.Conversation.Id, "history_read", JsonSerializer.SerializeToElement(new { sequence = 1, userOffset = 99999 }), 2048, default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.CallAsync(repository.Conversation.Id, "history_search", JsonSerializer.SerializeToElement(new { query = "orion" }), 2048, cancellation.Token));
    }

    private sealed class Repository : IChatRepository
    {
        public Conversation Conversation { get; } = Conversation.Create("history", "demo", "");
        public List<ChatMessage> Messages { get; } = [];
        public Repository(int turns)
        {
            for (var i = 0; i < turns; i++)
            {
                var request = Guid.NewGuid();
                Messages.Add(new(Conversation.Id, i * 2 + 1, request, "user", i == 0 ? "Décision stockage : /srv/orion." : "Message indépendant " + i));
                Messages.Add(new(Conversation.Id, i * 2 + 2, request, "assistant", "Proposition non validée : /tmp/cache."));
                Conversation.CompleteTurn();
            }
        }
        public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(id == Conversation.Id ? Conversation : null);
        public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.ConversationId == id && (!before.HasValue || m.Sequence < before)).TakeLast(limit).ToArray());
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Attachment>>([]);
        public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task AddAsync(Conversation c, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Conversation c, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid request, CancellationToken ct) => throw new NotSupportedException();
        public Task AppendTurnAsync(Conversation c, ChatMessage u, ChatMessage a, CancellationToken ct) => throw new NotSupportedException();
    }
}
