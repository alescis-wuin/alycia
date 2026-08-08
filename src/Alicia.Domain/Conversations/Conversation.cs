using System.Collections.ObjectModel;

namespace Alicia.Domain.Conversations;

public sealed class Conversation
{
    public const string DefaultTitle = "New conversation";
    public const int MaxTitleLength = 120;

    private readonly List<ChatMessage> _messages = [];
    private readonly ReadOnlyCollection<ChatMessage> _readOnlyMessages;

    public Conversation(ConversationId id, DateTimeOffset createdAt)
        : this(id, DefaultTitle, createdAt, createdAt)
    {
    }

    public Conversation(
        ConversationId id,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(id));
        }

        if (createdAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAt), createdAt, "Conversation creation time must be defined.");
        }

        if (updatedAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAt), updatedAt, "Conversation update time must be defined.");
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                updatedAt,
                "Conversation update time cannot predate its creation time.");
        }

        Id = id;
        Title = NormalizeTitle(title);
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        _readOnlyMessages = _messages.AsReadOnly();
    }

    public ConversationId Id { get; }

    public string Title { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ChatMessage> Messages => _readOnlyMessages;

    public void Rename(string title, DateTimeOffset renamedAt)
    {
        if (renamedAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(renamedAt), renamedAt, "Rename time must be defined.");
        }

        if (renamedAt < UpdatedAt)
        {
            throw new InvalidOperationException("A conversation cannot be renamed in the past.");
        }

        Title = NormalizeTitle(title);
        UpdatedAt = renamedAt;
    }

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

        if (message.CreatedAt > UpdatedAt)
        {
            UpdatedAt = message.CreatedAt;
        }
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Conversation title cannot be empty or whitespace.", nameof(title));
        }

        string normalizedTitle = title.Trim();

        if (normalizedTitle.Length > MaxTitleLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(title),
                normalizedTitle.Length,
                $"Conversation title cannot exceed {MaxTitleLength} characters.");
        }

        if (normalizedTitle.Any(char.IsControl))
        {
            throw new ArgumentException("Conversation title cannot contain control characters.", nameof(title));
        }

        return normalizedTitle;
    }
}
