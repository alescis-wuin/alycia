using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Alicia.Domain.Conversations;

public sealed record ConversationContextRevision
{
    private const string PayloadHashSchema = "alicia-conversation-context-payload-v1";

    public ConversationContextRevision(
        ConversationContextId contextId,
        ConversationContextRevisionId revisionId,
        ConversationContextRevisionId? parentRevisionId,
        string? instructions,
        bool replaceProfileInstructions,
        DateTimeOffset createdAt)
    {
        if (contextId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation-context identifier cannot be empty.",
                nameof(contextId));
        }

        if (revisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation-context revision identifier cannot be empty.",
                nameof(revisionId));
        }

        if (parentRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Parent conversation-context revision identifier cannot be empty when supplied.",
                nameof(parentRevisionId));
        }

        if (parentRevisionId == revisionId)
        {
            throw new ArgumentException(
                "A conversation-context revision cannot be its own parent.",
                nameof(parentRevisionId));
        }

        if (createdAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(createdAt),
                createdAt,
                "Conversation-context revision time must be defined.");
        }

        ContextId = contextId;
        RevisionId = revisionId;
        ParentRevisionId = parentRevisionId;
        Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions;
        ReplaceProfileInstructions = replaceProfileInstructions;
        CreatedAt = createdAt;
        PayloadHash = ComputePayloadHash();
    }

    public ConversationContextId ContextId { get; }

    public ConversationContextRevisionId RevisionId { get; }

    public ConversationContextRevisionId? ParentRevisionId { get; }

    public string? Instructions { get; }

    public bool ReplaceProfileInstructions { get; }

    public DateTimeOffset CreatedAt { get; }

    public string PayloadHash { get; }

    private string ComputePayloadHash()
    {
        StringBuilder canonical = new();
        AppendCanonical(canonical, PayloadHashSchema);
        AppendCanonical(canonical, ContextId.ToString());
        AppendCanonical(canonical, Instructions);
        AppendCanonical(canonical, ReplaceProfileInstructions ? "1" : "0");
        AppendCanonical(
            canonical,
            CreatedAt.ToString("O", CultureInfo.InvariantCulture));

        byte[] payload = Encoding.UTF8.GetBytes(canonical.ToString());
        byte[] digest = SHA256.HashData(payload);
        return Convert.ToHexString(digest);
    }

    private static void AppendCanonical(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:;");
            return;
        }

        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');
    }
}
