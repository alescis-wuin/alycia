using Alicia.Domain.Conversations;

namespace Alicia.Domain.Tests.Conversations;

public sealed class ConversationContextTests
{
    private static readonly DateTimeOffset _createdAt =
        new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorStartsWithoutConfirmedContext()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ConversationBranch root = Assert.Single(conversation.Branches);

        Assert.Equal(new ConversationContextId(conversation.Id.Value), conversation.ContextId);
        Assert.Empty(conversation.ContextRevisions);
        Assert.Null(conversation.ActiveContextRevision);
        Assert.Null(root.InheritedContextRevisionId);
        Assert.Empty(root.ContextBindings);
    }

    [Fact]
    public void UpdateContextCreatesSelfContainedRevisionAnchoredToCurrentBranch()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage user = new(
            MessageId.New(),
            MessageRole.User,
            "Question",
            _createdAt.AddMinutes(1));
        conversation.AddMessage(user);
        DateTimeOffset contextAt = _createdAt.AddMinutes(2);

        ConversationContextRevision revision = conversation.UpdateContext(
            "Prefer concise answers.",
            replaceProfileInstructions: false,
            contextAt);

        Assert.Equal(conversation.ContextId, revision.ContextId);
        Assert.Null(revision.ParentRevisionId);
        Assert.Equal("Prefer concise answers.", revision.Instructions);
        Assert.False(revision.ReplaceProfileInstructions);
        Assert.Equal(contextAt, revision.CreatedAt);
        Assert.Matches("^[0-9A-F]{64}$", revision.PayloadHash);
        Assert.Same(revision, conversation.ActiveContextRevision);
        Assert.Equal(contextAt, conversation.UpdatedAt);

        ConversationBranch root = Assert.Single(conversation.Branches);
        ConversationContextBinding binding = Assert.Single(root.ContextBindings);
        Assert.Equal(revision.RevisionId, binding.RevisionId);
        Assert.Equal(user.RevisionId, binding.AppliedAfterRevisionId);
    }

    [Fact]
    public void UpdateContextChainsRevisionsAndCanConfirmEmptyContext()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ConversationContextRevision first = conversation.UpdateContext(
            "Initial context",
            replaceProfileInstructions: true,
            _createdAt.AddMinutes(1));

        ConversationContextRevision second = conversation.UpdateContext(
            instructions: null,
            replaceProfileInstructions: false,
            _createdAt.AddMinutes(2));

        Assert.Equal(first.RevisionId, second.ParentRevisionId);
        Assert.Null(second.Instructions);
        Assert.False(second.ReplaceProfileInstructions);
        Assert.Same(second, conversation.ActiveContextRevision);
        Assert.Equal(2, conversation.ContextRevisions.Count);
        Assert.Equal(2, Assert.Single(conversation.Branches).ContextBindings.Count);
    }

    [Fact]
    public void EditOlderMessageInheritsContextEffectiveAtExactDivergence()
    {
        Conversation conversation = CreateTimelineWithTwoContexts(
            out ChatMessage secondUser,
            out ConversationContextRevision earlierContext,
            out ConversationContextRevision laterContext);
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;

        conversation.EditUserMessage(
            secondUser.RevisionId,
            "Edited second question",
            _createdAt.AddMinutes(7));

        ConversationBranch child = conversation.Branches.Single(
            branch => branch.Id == conversation.ActiveBranchId);
        Assert.Equal(originalBranchId, child.ParentBranchId);
        Assert.Equal(earlierContext.RevisionId, child.InheritedContextRevisionId);
        Assert.Same(earlierContext, conversation.ActiveContextRevision);
        Assert.NotEqual(laterContext.RevisionId, conversation.ActiveContextRevision?.RevisionId);
    }

    [Fact]
    public void ParentContextChangeAfterForkDoesNotMutateChildContext()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage firstUser = new(
            MessageId.New(),
            MessageRole.User,
            "First",
            _createdAt.AddMinutes(1));
        conversation.AddMessage(firstUser);
        ConversationContextRevision inherited = conversation.UpdateContext(
            "Inherited",
            false,
            _createdAt.AddMinutes(2));
        ChatMessage secondUser = new(
            MessageId.New(),
            MessageRole.User,
            "Second",
            _createdAt.AddMinutes(3));
        conversation.AddMessage(secondUser);
        ConversationBranchId parentBranchId = conversation.ActiveBranchId;
        conversation.EditUserMessage(
            secondUser.RevisionId,
            "Edited second",
            _createdAt.AddMinutes(4));
        ConversationBranchId childBranchId = conversation.ActiveBranchId;

        conversation.ActivateBranch(parentBranchId);
        ConversationContextRevision parentLater = conversation.UpdateContext(
            "Parent later",
            false,
            _createdAt.AddMinutes(5));
        conversation.ActivateBranch(childBranchId);

        Assert.Equal(inherited.RevisionId, conversation.ActiveContextRevision?.RevisionId);
        Assert.NotEqual(parentLater.RevisionId, conversation.ActiveContextRevision?.RevisionId);
    }

    [Fact]
    public void ChildContextCanDivergeIndependentlyFromParent()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        conversation.UpdateContext("Base", false, _createdAt.AddMinutes(1));
        ChatMessage user = new(
            MessageId.New(),
            MessageRole.User,
            "Question",
            _createdAt.AddMinutes(2));
        conversation.AddMessage(user);
        ConversationBranchId parentBranchId = conversation.ActiveBranchId;
        conversation.EditUserMessage(
            user.RevisionId,
            "Edited",
            _createdAt.AddMinutes(3));
        ConversationBranchId childBranchId = conversation.ActiveBranchId;
        ConversationContextRevision childContext = conversation.UpdateContext(
            "Child only",
            true,
            _createdAt.AddMinutes(4));

        conversation.ActivateBranch(parentBranchId);
        ConversationContextRevision? parentContext = conversation.ActiveContextRevision;
        conversation.ActivateBranch(childBranchId);

        Assert.Equal(childContext.RevisionId, conversation.ActiveContextRevision?.RevisionId);
        Assert.NotEqual(childContext.RevisionId, parentContext?.RevisionId);
    }

    [Fact]
    public void RestoreRejectsUnownedContextRevision()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationContextRevision orphan = new(
            new ConversationContextId(conversationId.Value),
            ConversationContextRevisionId.New(),
            parentRevisionId: null,
            "Orphan",
            false,
            _createdAt);
        ConversationBranch root = new(
            ConversationBranchId.New(),
            parentBranchId: null,
            forkedAfterRevisionId: null,
            localRevisionIds: Array.Empty<MessageRevisionId>());

        Assert.Throws<InvalidOperationException>(() => Conversation.Restore(
            conversationId,
            Conversation.DefaultTitle,
            _createdAt,
            _createdAt,
            messageRevisions: Array.Empty<ChatMessage>(),
            contextRevisions: new[] { orphan },
            branches: new[] { root },
            activeBranchId: root.Id));
    }

    [Fact]
    public void RestoreRejectsChildWithContextNotEffectiveAtFork()
    {
        ConversationId conversationId = ConversationId.New();
        ChatMessage first = new(
            MessageId.New(),
            MessageRole.User,
            "First",
            _createdAt.AddMinutes(1));
        ChatMessage second = new(
            MessageId.New(),
            MessageRole.User,
            "Second",
            _createdAt.AddMinutes(3));
        ChatMessage editedSecond = new(
            second.Id,
            MessageRevisionId.New(),
            second.RevisionId,
            MessageRole.User,
            "Edited second",
            _createdAt.AddMinutes(4));
        ConversationContextRevision beforeSecond = new(
            new ConversationContextId(conversationId.Value),
            ConversationContextRevisionId.New(),
            parentRevisionId: null,
            "Before second",
            false,
            _createdAt.AddMinutes(2));
        ConversationBranch root = new(
            ConversationBranchId.New(),
            null,
            null,
            new[] { first.RevisionId, second.RevisionId },
            inheritedContextRevisionId: null,
            contextBindings: new[]
            {
                new ConversationContextBinding(
                    beforeSecond.RevisionId,
                    first.RevisionId),
            });
        ConversationBranch child = new(
            ConversationBranchId.New(),
            root.Id,
            first.RevisionId,
            new[] { editedSecond.RevisionId },
            inheritedContextRevisionId: null,
            contextBindings: null);

        Assert.Throws<InvalidOperationException>(() => Conversation.Restore(
            conversationId,
            Conversation.DefaultTitle,
            _createdAt,
            _createdAt.AddMinutes(4),
            new[] { first, second, editedSecond },
            new[] { beforeSecond },
            new[] { root, child },
            child.Id));
    }

    private static Conversation CreateTimelineWithTwoContexts(
        out ChatMessage secondUser,
        out ConversationContextRevision earlierContext,
        out ConversationContextRevision laterContext)
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "First question",
            _createdAt.AddMinutes(1)));
        earlierContext = conversation.UpdateContext(
            "Earlier",
            false,
            _createdAt.AddMinutes(2));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "First answer",
            _createdAt.AddMinutes(3)));
        secondUser = new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Second question",
            _createdAt.AddMinutes(4));
        conversation.AddMessage(secondUser);
        laterContext = conversation.UpdateContext(
            "Later",
            false,
            _createdAt.AddMinutes(5));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Second answer",
            _createdAt.AddMinutes(6)));
        return conversation;
    }
}
