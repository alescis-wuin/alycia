using System.Collections.ObjectModel;

namespace Alicia.Domain.Conversations;

public sealed class Conversation
{
    private readonly List<ChatMessage> _messages = [];
    private readonly ReadOnlyCollection<ChatMessage> _readOnlyMessages;

    public Conversation(ConversationId id, DateTimeOffset createdAt)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(id));
        }

        if (createdAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAt), createdAt, "Conversation creation time must be defined.");
        }

        Id = id;
        CreatedAt = createdAt;
        _readOnlyMessages = _messages.AsReadOnly();
    }

    public ConversationId Id { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<ChatMessage> Messages => _readOnlyMessages;

    public void AddMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.CreatedAt < CreatedAt)
        {
            throw new InvalidOperationException("A message cannot predate its conversation.");
        }

        if (_messages.Any(existing => existing.Id == message.Id))
        {
            throw new InvalidOperationException($"Message '{message.Id}' already belongs to the conversation.");
        }

        _messages.Add(message);
    }
}
