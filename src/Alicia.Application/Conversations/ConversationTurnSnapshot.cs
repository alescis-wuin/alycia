using System.Collections.ObjectModel;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

internal sealed class ConversationTurnSnapshot
{
    private readonly ChatMessage[] _messageRevisions;
    private readonly ConversationContextRevision[] _contextRevisions;
    private readonly ConversationBranchSnapshot[] _branches;
    private readonly ReadOnlyCollection<MessageRevisionId> _inputMessageRevisionIds;

    private ConversationTurnSnapshot(
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        ConversationBranchId activeBranchId,
        ChatMessage[] activeMessages,
        ChatMessage[] messageRevisions,
        ConversationContextRevision[] contextRevisions,
        ConversationBranchSnapshot[] branches,
        MessageRevisionId triggeringUserMessageRevisionId,
        ConversationContextRevision? activeContextRevision)
    {
        Title = title;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        ActiveBranchId = activeBranchId;
        _messageRevisions = messageRevisions;
        _contextRevisions = contextRevisions;
        _branches = branches;
        TriggeringUserMessageRevisionId = triggeringUserMessageRevisionId;
        ActiveContextRevision = activeContextRevision;
        _inputMessageRevisionIds = Array.AsReadOnly(
            activeMessages.Select(message => message.RevisionId).ToArray());
    }

    private string Title { get; }

    private DateTimeOffset CreatedAt { get; }

    private DateTimeOffset UpdatedAt { get; }

    public ConversationBranchId ActiveBranchId { get; }

    public MessageRevisionId TriggeringUserMessageRevisionId { get; }

    public IReadOnlyList<MessageRevisionId> InputMessageRevisionIds => _inputMessageRevisionIds;

    public ConversationContextRevision? ActiveContextRevision { get; }

    public static ConversationTurnSnapshot Capture(
        Conversation conversation,
        MessageId triggeringUserMessageId)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ChatMessage trigger = ValidateTrigger(conversation, triggeringUserMessageId);
        ChatMessage[] activeMessages = conversation.Messages.ToArray();

        return new ConversationTurnSnapshot(
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.ActiveBranchId,
            activeMessages,
            conversation.MessageRevisions.ToArray(),
            conversation.ContextRevisions.ToArray(),
            conversation.Branches.Select(ConversationBranchSnapshot.Capture).ToArray(),
            trigger.RevisionId,
            conversation.ActiveContextRevision);
    }

    public Conversation RequireMatching(Conversation? conversation)
    {
        if (conversation is null || !Matches(conversation))
        {
            throw new InvalidOperationException(
                "Conversation changed while Alicia was responding. Retry from the latest history.");
        }

        return conversation;
    }

    private bool Matches(Conversation conversation)
    {
        if (!string.Equals(
                Title,
                conversation.Title,
                StringComparison.Ordinal)
            || CreatedAt != conversation.CreatedAt
            || UpdatedAt != conversation.UpdatedAt
            || ActiveBranchId != conversation.ActiveBranchId
            || !_messageRevisions.SequenceEqual(conversation.MessageRevisions)
            || !_contextRevisions.SequenceEqual(conversation.ContextRevisions)
            || _branches.Length != conversation.Branches.Count)
        {
            return false;
        }

        for (int index = 0; index < _branches.Length; index++)
        {
            if (!_branches[index].Matches(conversation.Branches[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static ChatMessage ValidateTrigger(
        Conversation conversation,
        MessageId triggeringUserMessageId)
    {
        int triggerIndex = -1;

        for (int index = 0; index < conversation.Messages.Count; index++)
        {
            if (conversation.Messages[index].Id == triggeringUserMessageId)
            {
                triggerIndex = index;
                break;
            }
        }

        if (triggerIndex < 0)
        {
            throw new KeyNotFoundException(
                $"Message '{triggeringUserMessageId}' was not found in conversation '{conversation.Id}'.");
        }

        ChatMessage trigger = conversation.Messages[triggerIndex];

        if (trigger.Role != MessageRole.User)
        {
            throw new InvalidOperationException(
                "Only a user message can trigger an assistant response.");
        }

        if (triggerIndex != conversation.Messages.Count - 1)
        {
            throw new InvalidOperationException(
                "The triggering user message is no longer the latest unanswered message.");
        }

        return trigger;
    }

    private sealed class ConversationBranchSnapshot
    {
        private readonly MessageRevisionId[] _localRevisionIds;
        private readonly ConversationContextBinding[] _contextBindings;

        private ConversationBranchSnapshot(
            ConversationBranchId id,
            ConversationBranchId? parentBranchId,
            MessageRevisionId? forkedAfterRevisionId,
            ConversationContextRevisionId? inheritedContextRevisionId,
            MessageRevisionId[] localRevisionIds,
            ConversationContextBinding[] contextBindings)
        {
            Id = id;
            ParentBranchId = parentBranchId;
            ForkedAfterRevisionId = forkedAfterRevisionId;
            InheritedContextRevisionId = inheritedContextRevisionId;
            _localRevisionIds = localRevisionIds;
            _contextBindings = contextBindings;
        }

        private ConversationBranchId Id { get; }

        private ConversationBranchId? ParentBranchId { get; }

        private MessageRevisionId? ForkedAfterRevisionId { get; }

        private ConversationContextRevisionId? InheritedContextRevisionId { get; }

        public static ConversationBranchSnapshot Capture(ConversationBranch branch)
        {
            ArgumentNullException.ThrowIfNull(branch);

            return new ConversationBranchSnapshot(
                branch.Id,
                branch.ParentBranchId,
                branch.ForkedAfterRevisionId,
                branch.InheritedContextRevisionId,
                branch.LocalRevisionIds.ToArray(),
                branch.ContextBindings.ToArray());
        }

        public bool Matches(ConversationBranch branch)
        {
            ArgumentNullException.ThrowIfNull(branch);

            return Id == branch.Id
                && ParentBranchId == branch.ParentBranchId
                && ForkedAfterRevisionId == branch.ForkedAfterRevisionId
                && InheritedContextRevisionId == branch.InheritedContextRevisionId
                && _localRevisionIds.SequenceEqual(branch.LocalRevisionIds)
                && _contextBindings.SequenceEqual(branch.ContextBindings);
        }
    }
}
