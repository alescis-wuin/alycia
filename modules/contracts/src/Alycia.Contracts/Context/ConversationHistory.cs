using Alycia.Contracts.Documents;

namespace Alycia.Contracts.Context;

public sealed record HistoryMessage(Guid ConversationId, int Sequence, Guid RequestId, string Role,
    string Content, DateTimeOffset CreatedAtUtc);

// The conversation owner has already resolved project notes and routing instructions.
public sealed record ContextConversation(Guid Id, int MessageCount, string Model, string SystemText,
    string MemoryText, int MemoryThroughSequence, string MemorySourceHash, string MemoryModel, string MemoryLedgerJson);

public interface ITurnHistory
{
    // Ascending sequence. The cursor is exclusive; the last limit messages are returned.
    Task<IReadOnlyList<HistoryMessage>> RecentAsync(Guid id, int limit, int? before, CancellationToken ct);
    // Ascending sequence in (after, through], in the same conversation and transaction.
    Task<IReadOnlyList<HistoryMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct);
}

public interface IConversationContextReader : ITurnHistory
{
    Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid id, int first, CancellationToken ct);
}
