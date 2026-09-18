using System.Collections.ObjectModel;

namespace Alicia.Domain.Conversations;

public sealed class ConversationBranch
{
    private readonly ReadOnlyCollection<MessageRevisionId> _localRevisionIds;
    private readonly ReadOnlyCollection<ConversationContextBinding> _contextBindings;

    public ConversationBranch(
        ConversationBranchId id,
        ConversationBranchId? parentBranchId,
        MessageRevisionId? forkedAfterRevisionId,
        IEnumerable<MessageRevisionId> localRevisionIds)
        : this(
            id,
            parentBranchId,
            forkedAfterRevisionId,
            localRevisionIds,
            inheritedContextRevisionId: null,
            contextBindings: null)
    {
    }

    public ConversationBranch(
        ConversationBranchId id,
        ConversationBranchId? parentBranchId,
        MessageRevisionId? forkedAfterRevisionId,
        IEnumerable<MessageRevisionId> localRevisionIds,
        ConversationContextRevisionId? inheritedContextRevisionId,
        IEnumerable<ConversationContextBinding>? contextBindings)
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

        if (inheritedContextRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Inherited conversation-context revision identifier cannot be empty when supplied.",
                nameof(inheritedContextRevisionId));
        }

        if (parentBranchId is null && forkedAfterRevisionId is not null)
        {
            throw new ArgumentException(
                "A root conversation branch cannot define a fork revision.",
                nameof(forkedAfterRevisionId));
        }

        if (parentBranchId is null && inheritedContextRevisionId is not null)
        {
            throw new ArgumentException(
                "A root conversation branch cannot inherit a conversation-context revision.",
                nameof(inheritedContextRevisionId));
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

        ConversationContextBinding[] bindings = contextBindings?.ToArray()
            ?? Array.Empty<ConversationContextBinding>();

        if (bindings.Any(binding => binding is null))
        {
            throw new ArgumentException(
                "Conversation branch context bindings cannot contain null entries.",
                nameof(contextBindings));
        }

        if (bindings.Select(binding => binding.RevisionId).Distinct().Count() != bindings.Length)
        {
            throw new ArgumentException(
                "Conversation branch context bindings cannot own the same context revision more than once.",
                nameof(contextBindings));
        }

        Id = id;
        ParentBranchId = parentBranchId;
        ForkedAfterRevisionId = forkedAfterRevisionId;
        InheritedContextRevisionId = inheritedContextRevisionId;
        _localRevisionIds = Array.AsReadOnly(revisions);
        _contextBindings = Array.AsReadOnly(bindings);
    }

    public ConversationBranchId Id { get; }

    public ConversationBranchId? ParentBranchId { get; }

    public MessageRevisionId? ForkedAfterRevisionId { get; }

    public ConversationContextRevisionId? InheritedContextRevisionId { get; }

    public IReadOnlyList<MessageRevisionId> LocalRevisionIds => _localRevisionIds;

    public IReadOnlyList<ConversationContextBinding> ContextBindings => _contextBindings;

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
            revisions,
            InheritedContextRevisionId,
            _contextBindings);
    }

    internal ConversationBranch AppendContext(ConversationContextBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        ConversationContextBinding[] bindings =
            new ConversationContextBinding[_contextBindings.Count + 1];
        _contextBindings.CopyTo(bindings, 0);
        bindings[^1] = binding;

        return new ConversationBranch(
            Id,
            ParentBranchId,
            ForkedAfterRevisionId,
            _localRevisionIds,
            InheritedContextRevisionId,
            bindings);
    }
}
