using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public interface IConversationMessagesReader
{
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct);
}

public interface IConversationContextReader : IConversationMessagesReader
{
    Task<IReadOnlyList<Attachment>> ContextFilesAsync(Guid id, int first, CancellationToken ct);
}

public interface IConversationHistoryReader : IConversationMessagesReader
{
    Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken);
}
