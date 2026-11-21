using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public interface IConversationContextReader
{
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken cancellationToken);
    Task<IReadOnlyList<Attachment>> ContextFilesAsync(Guid id, int first, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct);
}
