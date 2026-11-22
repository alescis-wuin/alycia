using Alycia.Contracts.Context;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Tools;
using Alycia.Contracts.Memory;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Chat.Tests;

public sealed class TurnReadContractTests
{
    [PostgresFact]
    public Task Scoped_history_and_documents_share_paging_visibility_immutability_and_cancellation() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var owner = Conversation.Create("A", "local", "");
            var other = Conversation.Create("B", "local", "");
            var empty = Conversation.Create("C", "local", "");
            db.AddRange(owner, other, empty);
            var instant = DateTimeOffset.UnixEpoch.AddDays(12);
            var messages = new List<HistoryMessage>();
            foreach (var conversation in new[] { owner, other, owner })
            {
                var request = Guid.NewGuid();
                foreach (var role in new[] { "user", "assistant" })
                {
                    var sequence = conversation.MessageCount + (role == "user" ? 1 : 2);
                    var content = $"{conversation.Title}-{sequence}";
                    var date = instant.AddSeconds(sequence);
                    messages.Add(new(conversation.Id, sequence, request, role, content, date));
                    db.Add(ChatMessage.Import(conversation.Id, sequence, request, role, content, date));
                }
                conversation.CompleteTurn();
            }
            var scoped = new List<(Guid, TurnFile)>();
            foreach (var (conversation, name, mode, anchor) in new[] {
                (owner, "pinned", "pinned", 1), (owner, "old", "window", 1), (owner, "hidden", "excluded", 4),
                (owner, "visible", "window", 4), (other, "foreign", "pinned", 1) })
            {
                var file = new Attachment
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Name = name,
                    Text = name,
                    Size = name.Length,
                    Sha256 = MemoryTransfer.Hash(name),
                    ContextMode = mode,
                    BatchId = Guid.NewGuid(),
                    Status = "ready",
                    CreatedAtUtc = instant.AddMinutes(scoped.Count),
                    AnchorSequence = anchor
                };
                db.Add(file);
                scoped.Add((conversation.Id, new(file.Id, null, conversation.Id, file.BatchId, null, name, file.Size,
                    file.Sha256, file.Status, name, anchor, file.CreatedAtUtc, "", "", file.MetadataSource, mode)));
            }
            await db.SaveChangesAsync();
            var documents = new TurnDocuments(db, ChatFixture.DocumentScope);
            var reads = new ConversationReadModel(db, documents);
            var mock = new SnapshotTurnReads([owner.Id, other.Id, empty.Id], messages, scoped);
            var temporalMock = new SnapshotTemporalHistory(
                [new(owner.Id, 4, "{}"), new(other.Id, 2, "{}")], messages);
            foreach (ITemporalHistorySources temporal in new ITemporalHistorySources[] {
                new TemporalHistorySources(db, new ChatOptions { TemporalMemoryEnabled = true }), temporalMock })
            {
                var ordered = new[] { owner.Id, other.Id }.Order().ToArray();
                Assert.Equal(ordered[0], Assert.Single(await temporal.OwnersAsync(null, 1, default)).Id);
                Assert.Equal(ordered[1], Assert.Single(await temporal.OwnersAsync(ordered[0], 1, default)).Id);
                Assert.Empty(await temporal.OwnersAsync(ordered[1], 1, default));
                var numbers = await temporal.SequencesAsync(owner.Id, 1, 3, default);
                Assert.Equal([2, 3], numbers);
                var selected = await temporal.MessagesAsync(owner.Id, [4, 1, 4], default);
                Assert.Equal(["A-1", "A-4"], selected.Select(m => m.Content));
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => temporal.MessagesAsync(owner.Id, new int[33], default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => temporal.OwnersAsync(null, 1, new(true)));
            }
            messages.Clear();
            scoped.Clear();
            foreach (var (context, tools, query, files) in new (IConversationContextReader, IToolHistoryReader, IQuerySources, ITurnDocuments)[]
                { (reads, reads, reads, documents), (mock, mock, mock, mock) })
            {
                ITurnHistory history = context;
                var page = await history.RecentAsync(owner.Id, 2, 4, default);
                Assert.Equal([2, 3], page.Select(m => m.Sequence));
                Assert.All(page, m => Assert.Equal(owner.Id, m.ConversationId));
                Assert.Throws<NotSupportedException>(() => ((IList<HistoryMessage>)page)[0] = page[1]);
                Assert.Equal([2, 3, 4], (await history.SummaryRangeAsync(owner.Id, 1, 4, default)).Select(m => m.Sequence));
                Assert.Equal([3, 4], (await query.RecentForQueryAsync(owner.Id, 2, default)).Select(m => m.Sequence));
                Assert.Equal(4, await tools.MessageCountAsync(owner.Id, default));
                Assert.Equal(0, await tools.MessageCountAsync(empty.Id, default));
                Assert.Null(await tools.MessageCountAsync(Guid.Empty, default));
                Assert.Empty(await history.RecentAsync(Guid.Empty, 10, null, default));
                Assert.Equal(["pinned", "old", "hidden", "visible"], (await files.FilesAsync(owner.Id, default)).Select(f => f.Name));
                Assert.Equal(["pinned", "visible"], (await context.ContextFilesAsync(owner.Id, 3, default)).Select(f => f.Name));
                Assert.Equal(["foreign"], (await query.ContextFilesAsync(other.Id, 3, default)).Select(f => f.Name));
                var cancelled = new CancellationToken(true);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.FilesAsync(owner.Id, cancelled));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.MessageCountAsync(owner.Id, cancelled));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => history.SummaryRangeAsync(owner.Id, 1, 3, cancelled));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.RecentForQueryAsync(owner.Id, 1, cancelled));
            }
        });
}
