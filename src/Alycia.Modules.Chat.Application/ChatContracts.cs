using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public interface IChatRepository
{
    Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken);
    Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken cancellationToken);
    Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant, CancellationToken cancellationToken);
}

public sealed record PromptMessage(string Role, string Content);
public sealed record ContextWindow(IReadOnlyList<PromptMessage> Messages, int HistoryMessagesIncluded, int EstimatedInputTokens, int FilesIncluded = 0);
public sealed record SendMessageResult(IReadOnlyList<ChatMessage> Messages, int? HistoryMessagesIncluded, bool Replayed, int FilesIncluded = 0);

public interface IChatModel
{
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken cancellationToken);
}

public sealed class ChatConflictException() : Exception("La conversation a changé. Rechargez-la puis réessayez.");
public sealed class ConversationNotFoundException() : Exception("Conversation introuvable.");
public sealed class ChatModelException(string message) : Exception(message);

public sealed class ChatOptions
{
    public int MaxHistoryTurns { get; set; } = 12;
    public int ContextTokens { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 1024;
    public int SafetyTokens { get; set; } = 256;
    public int TimeoutSeconds { get; set; } = 180;

    public void Validate()
    {
        if (MaxHistoryTurns is < 0 or > 100 || ContextTokens is < 1024 or > 131072
            || MaxOutputTokens < 1 || SafetyTokens < 128
            || (long)MaxOutputTokens + SafetyTokens >= ContextTokens
            || TimeoutSeconds is < 1 or > 600)
        {
            throw new ArgumentException("Configuration Chat invalide : vérifiez fenêtre, contexte, réserve et délai.");
        }
    }
}
