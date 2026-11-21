using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Context;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Tools;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

internal static class TestTurnSnapshots
{
    public static HistoryMessage ToHistory(this ChatMessage m) =>
        new(m.ConversationId, m.Sequence, m.RequestId, m.Role, m.Content, m.CreatedAtUtc);

    public static Alycia.Contracts.Memory.TemporalMessage ToTemporal(this ChatMessage m) =>
        new(m.Sequence, m.Role, m.Content, m.CreatedAtUtc);

    public static TurnFile ToTurnFile(this Attachment f) =>
        new(f.Id, f.WorkspaceId, f.ConversationId, f.BatchId, f.ArchiveId, f.Name, f.Size, f.Sha256,
            f.Status, f.Text, f.AnchorSequence, f.CreatedAtUtc, f.SourceCreatedAt, f.SourceModifiedAt,
            f.MetadataSource, f.ContextMode);
}

internal interface ITestConversationRepository : IChatRepository
{
    Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct);

    async Task<int?> IToolHistoryReader.MessageCountAsync(Guid id, CancellationToken ct) =>
        (await FindAsync(id, ct))?.MessageCount;

    async Task<IReadOnlyList<HistoryMessage>> ITurnHistory.RecentAsync(Guid id, int limit, int? before, CancellationToken ct) =>
        (await MessagesAsync(id, limit, before, ct)).Select(m => m.ToHistory()).ToArray();

    async Task<IReadOnlyList<HistoryMessage>> ITurnHistory.SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
        (await MessagesAsync(id, through - after, through + 1, ct)).Where(m => m.Sequence > after && m.Sequence <= through)
            .Select(m => m.ToHistory()).ToArray();

    async Task<IReadOnlyList<QueryHistoryItem>> IQuerySources.RecentForQueryAsync(Guid id, int limit, CancellationToken ct) =>
        (await MessagesAsync(id, limit, null, ct)).Select(m => new QueryHistoryItem(m.ConversationId, m.Sequence, m.Role, m.Content)).ToArray();

    Task<IReadOnlyList<TurnFile>> IQuerySources.ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        ((IConversationContextReader)this).ContextFilesAsync(id, first, ct);

    async Task<IReadOnlyList<TurnFile>> IConversationContextReader.ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        (await FilesAsync(id, ct)).Where(f => f.ContextMode != "excluded" &&
            (f.ContextMode is "pinned" or "project" || f.AnchorSequence >= first)).Select(f => f.ToTurnFile()).ToArray();
}
