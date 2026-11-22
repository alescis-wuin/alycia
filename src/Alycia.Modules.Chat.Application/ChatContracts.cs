using Alycia.Contracts.Tools;
using Alycia.Contracts.Context;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public interface IChatTurnScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public interface IChatRepository : IConversationContextReader, IToolHistoryReader, IQuerySources
{
    Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IChatTurnScope?>(null);
    }
    Task<Conversation?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken cancellationToken);
    Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant, CancellationToken cancellationToken);
}

public sealed record SendMessageResult(IReadOnlyList<ChatMessage> Messages, int? HistoryMessagesIncluded,
    bool Replayed, int FilesIncluded = 0, int? EstimatedInputTokens = null, GenerationStats? Stats = null, GenerationUsage? Usage = null);
