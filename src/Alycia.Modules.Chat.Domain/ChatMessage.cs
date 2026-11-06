namespace Alycia.Modules.Chat.Domain;

public sealed class ChatMessage
{
    public Guid ConversationId { get; private set; }
    public int Sequence { get; private set; }
    public Guid RequestId { get; private set; }
    public string Role { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private ChatMessage() { }

    public ChatMessage(Guid conversationId, int sequence, Guid requestId, string role, string content)
    {
        if (conversationId == Guid.Empty || requestId == Guid.Empty || sequence < 1
            || role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(content)
            || content.Contains('\0'))
        {
            throw new ArgumentException("Message invalide.");
        }

        ConversationId = conversationId;
        Sequence = sequence;
        RequestId = requestId;
        Role = role;
        Content = content;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
}
