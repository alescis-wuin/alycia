namespace Alicia.Domain.Conversations;

public sealed record ConversationContextBinding
{
    public ConversationContextBinding(
        ConversationContextRevisionId revisionId,
        MessageRevisionId? appliedAfterRevisionId)
    {
        if (revisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation-context revision identifier cannot be empty.",
                nameof(revisionId));
        }

        if (appliedAfterRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Context binding message revision identifier cannot be empty when supplied.",
                nameof(appliedAfterRevisionId));
        }

        RevisionId = revisionId;
        AppliedAfterRevisionId = appliedAfterRevisionId;
    }

    public ConversationContextRevisionId RevisionId { get; }

    public MessageRevisionId? AppliedAfterRevisionId { get; }
}
