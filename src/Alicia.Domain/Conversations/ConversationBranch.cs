using System.Collections.ObjectModel;

namespace Alicia.Domain.Conversations;

public sealed class ConversationBranch
{
    private readonly ReadOnlyCollection<MessageRevisionId> _localRevisionIds;

    public ConversationBranch(
        ConversationBranchId id,
        ConversationBranchId? parentBranchId,
        MessageRevisionId? forkedAfterRevisionId,
        IEnumerable<MessageRevisionId> localRevisionIds)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Conversation branch identifier cannot be empty.", nameof(id));
        }

        if (parentBranchId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Parent conversation branch identifier cannot be empty when supplied.",
                nameof(parentBranchId));
        }

        if (parentBranchId == id)
        {
            throw new ArgumentException(
                "A conversation branch cannot be its own parent.",
                nameof(parentBranchId));
        }

        if (forkedAfterRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Fork revision identifier cannot be empty when supplied.",
                nameof(forkedAfterRevisionId));
        }

        if (parentBranchId is null && forkedAfterRevisionId is not null)
        {
            throw new ArgumentException(
                "A root conversation branch cannot define a fork revision.",
                nameof(forkedAfterRevisionId));
        }

        ArgumentNullException.ThrowIfNull(localRevisionIds);
        MessageRevisionId[] revisions = localRevisionIds.ToArray();

        if (revisions.Any(revisionId => revisionId.IsEmpty))
        {
            throw new ArgumentException(
                "Conversation branch revisions cannot contain an empty identifier.",
                nameof(localRevisionIds));
        }

        if (revisions.Distinct().Count() != revisions.Length)
        {
            throw new ArgumentException(
                "Conversation branch revisions cannot contain duplicate identifiers.",
                nameof(localRevisionIds));
        }

        Id = id;
        ParentBranchId = parentBranchId;
        ForkedAfterRevisionId = forkedAfterRevisionId;
        _localRevisionIds = Array.AsReadOnly(revisions);
    }

    public ConversationBranchId Id { get; }

    public ConversationBranchId? ParentBranchId { get; }

    public MessageRevisionId? ForkedAfterRevisionId { get; }

    public IReadOnlyList<MessageRevisionId> LocalRevisionIds => _localRevisionIds;

    internal ConversationBranch Append(MessageRevisionId revisionId)
    {
        if (revisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Message revision identifier cannot be empty.",
                nameof(revisionId));
        }

        MessageRevisionId[] revisions = new MessageRevisionId[_localRevisionIds.Count + 1];
        _localRevisionIds.CopyTo(revisions, 0);
        revisions[^1] = revisionId;

        return new ConversationBranch(
            Id,
            ParentBranchId,
            ForkedAfterRevisionId,
            revisions);
    }
}
