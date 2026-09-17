using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Alicia.Domain.Conversations;

public sealed record ChatMessage
{
    private const string PayloadHashSchema = "alicia-message-revision-payload-v1";

    public ChatMessage(
        MessageId id,
        MessageRole role,
        string content,
        DateTimeOffset createdAt)
        : this(
            id,
            MessageRevisionId.New(),
            parentRevisionId: null,
            role,
            content,
            createdAt)
    {
    }

    public ChatMessage(
        MessageId id,
        MessageRevisionId revisionId,
        MessageRevisionId? parentRevisionId,
        MessageRole role,
        string content,
        DateTimeOffset createdAt)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Message identifier cannot be empty.", nameof(id));
        }

        if (revisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Message revision identifier cannot be empty.",
                nameof(revisionId));
        }

        if (parentRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Parent message revision identifier cannot be empty when supplied.",
                nameof(parentRevisionId));
        }

        if (parentRevisionId == revisionId)
        {
            throw new ArgumentException(
                "A message revision cannot be its own parent.",
                nameof(parentRevisionId));
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
        RevisionId = revisionId;
        ParentRevisionId = parentRevisionId;
        Role = role;
        Content = content;
        CreatedAt = createdAt;
        PayloadHash = ComputePayloadHash(id, role, content, createdAt);
    }

    public MessageId Id { get; }

    public MessageRevisionId RevisionId { get; }

    public MessageRevisionId? ParentRevisionId { get; }

    public MessageRole Role { get; }

    public string Content { get; }

    public DateTimeOffset CreatedAt { get; }

    public string PayloadHash { get; }

    private static string ComputePayloadHash(
        MessageId id,
        MessageRole role,
        string content,
        DateTimeOffset createdAt)
    {
        StringBuilder canonical = new();
        AppendCanonical(canonical, PayloadHashSchema);
        AppendCanonical(canonical, id.ToString());
        AppendCanonical(canonical, ((int)role).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, content);
        AppendCanonical(
            canonical,
            createdAt.ToString("O", CultureInfo.InvariantCulture));

        byte[] payload = Encoding.UTF8.GetBytes(canonical.ToString());
        byte[] digest = SHA256.HashData(payload);
        return Convert.ToHexString(digest);
    }

    private static void AppendCanonical(StringBuilder builder, string value)
    {
        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');
    }
}
