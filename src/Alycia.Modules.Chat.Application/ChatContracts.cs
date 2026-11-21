using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public interface IChatTurnScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public interface IChatRepository
{
    Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct) => Task.FromResult<IChatTurnScope?>(null);
    Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken cancellationToken);
    async Task<IReadOnlyList<Attachment>> ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        (await FilesAsync(id, ct)).Where(f => f.ContextMode != "excluded"
            && ((f.ContextMode == "pinned" || f.ContextMode == "project") || f.AnchorSequence >= first)).ToArray();
    async Task<IReadOnlyList<ChatMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
        (await MessagesAsync(id, through - after, through + 1, ct)).Where(m => m.Sequence > after && m.Sequence <= through).ToArray();
    Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken);
    Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken cancellationToken);
    Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant, CancellationToken cancellationToken);
}

public sealed record SendMessageResult(IReadOnlyList<ChatMessage> Messages, int? HistoryMessagesIncluded,
    bool Replayed, int FilesIncluded = 0, int? EstimatedInputTokens = null, GenerationStats? Stats = null, GenerationUsage? Usage = null);

public sealed class ChatConflictException() : Exception("La conversation a changé. Rechargez-la puis réessayez.");
public sealed class ConversationNotFoundException() : Exception("Conversation introuvable.");
