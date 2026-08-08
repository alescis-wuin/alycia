namespace Alicia.Domain.Conversations;

public sealed record ChatMessage
{
    public ChatMessage(
        MessageId id,
        MessageRole role,
        string content,
        DateTimeOffset createdAt)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Message identifier cannot be empty.", nameof(id));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Message role is not defined.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message content cannot be empty or whitespace.", nameof(content));
        }

        if (createdAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAt), createdAt, "Message creation time must be defined.");
        }

        Id = id;
        Role = role;
        Content = content;
        CreatedAt = createdAt;
    }

    public MessageId Id { get; }

    public MessageRole Role { get; }

    public string Content { get; }

    public DateTimeOffset CreatedAt { get; }
}
