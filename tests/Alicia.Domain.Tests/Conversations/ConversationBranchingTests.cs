using Alicia.Domain.Conversations;

namespace Alicia.Domain.Tests.Conversations;

public sealed class ConversationBranchingTests
{
    private static readonly DateTimeOffset _createdAt =
        new(2026, 9, 17, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCreatesEmptyRootBranch()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);

        ConversationBranch root = Assert.Single(conversation.Branches);
        Assert.Equal(root.Id, conversation.ActiveBranchId);
        Assert.Null(root.ParentBranchId);
        Assert.Null(root.ForkedAfterRevisionId);
        Assert.Empty(root.LocalRevisionIds);
        Assert.Empty(conversation.MessageRevisions);
        Assert.Empty(conversation.Messages);
    }

    [Fact]
    public void EditUserMessageForksAndPreservesOriginalBranch()
    {
        Conversation conversation = CreateAnsweredConversation();
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        ChatMessage[] originalMessages = conversation.Messages.ToArray();
        ChatMessage target = originalMessages[2];
        DateTimeOffset editedAt = _createdAt.AddMinutes(5);

        ChatMessage edited = conversation.EditUserMessage(
            target.RevisionId,
            "Updated second question",
            editedAt);

        Assert.NotEqual(originalBranchId, conversation.ActiveBranchId);
        Assert.Equal(target.Id, edited.Id);
        Assert.NotEqual(target.RevisionId, edited.RevisionId);
        Assert.Equal(target.RevisionId, edited.ParentRevisionId);
        Assert.Equal(MessageRole.User, edited.Role);
        Assert.Equal("Updated second question", edited.Content);
        Assert.Equal(editedAt, edited.CreatedAt);
        Assert.Equal(5, conversation.MessageRevisions.Count);

        Assert.Collection(
            conversation.GetMessages(originalBranchId),
            message => Assert.Same(originalMessages[0], message),
            message => Assert.Same(originalMessages[1], message),
            message => Assert.Same(originalMessages[2], message),
            message => Assert.Same(originalMessages[3], message));
        Assert.Collection(
            conversation.Messages,
            message => Assert.Same(originalMessages[0], message),
            message => Assert.Same(originalMessages[1], message),
            message => Assert.Same(edited, message));

        ConversationBranch child = conversation.Branches.Single(
            branch => branch.Id == conversation.ActiveBranchId);
        Assert.Equal(originalBranchId, child.ParentBranchId);
        Assert.Equal(originalMessages[1].RevisionId, child.ForkedAfterRevisionId);
        Assert.Equal(new[] { edited.RevisionId }, child.LocalRevisionIds);
        Assert.Equal(editedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void ActivateBranchRestoresOriginalHistoryWithoutChangingActivity()
    {
        Conversation conversation = CreateAnsweredConversation();
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        ChatMessage target = conversation.Messages[2];
        conversation.EditUserMessage(
            target.RevisionId,
            "Updated",
            _createdAt.AddMinutes(5));
        ConversationBranchId editedBranchId = conversation.ActiveBranchId;
        DateTimeOffset updatedAt = conversation.UpdatedAt;

        conversation.ActivateBranch(originalBranchId);

        Assert.Equal(originalBranchId, conversation.ActiveBranchId);
        Assert.Equal(4, conversation.Messages.Count);
        Assert.Equal("Second question", conversation.Messages[2].Content);
        Assert.Equal(updatedAt, conversation.UpdatedAt);

        conversation.ActivateBranch(editedBranchId);
        Assert.Equal("Updated", conversation.Messages[^1].Content);
        Assert.Equal(updatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void AppendAfterEditExtendsOnlyChildBranch()
    {
        Conversation conversation = CreateAnsweredConversation();
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        ChatMessage target = conversation.Messages[2];
        ChatMessage edited = conversation.EditUserMessage(
            target.RevisionId,
            "Updated",
            _createdAt.AddMinutes(5));
        ChatMessage assistant = new(
            MessageId.New(),
            MessageRole.Assistant,
            "New answer",
            _createdAt.AddMinutes(6));

        conversation.AddMessage(assistant);

        Assert.Collection(
            conversation.Messages,
            message => Assert.Equal("First question", message.Content),
            message => Assert.Equal("First answer", message.Content),
            message => Assert.Same(edited, message),
            message => Assert.Same(assistant, message));
        Assert.Equal(4, conversation.GetMessages(originalBranchId).Count);
        Assert.Equal("Second answer", conversation.GetMessages(originalBranchId)[^1].Content);
    }

    [Fact]
    public void ParentExtensionAfterForkDoesNotChangeChildPrefix()
    {
        Conversation conversation = CreateAnsweredConversation();
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        ChatMessage target = conversation.Messages[2];
        ChatMessage edited = conversation.EditUserMessage(
            target.RevisionId,
            "Updated",
            _createdAt.AddMinutes(5));
        ConversationBranchId childBranchId = conversation.ActiveBranchId;

        conversation.ActivateBranch(originalBranchId);
        ChatMessage laterParentMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Parent-only continuation",
            _createdAt.AddMinutes(6));
        conversation.AddMessage(laterParentMessage);
        conversation.ActivateBranch(childBranchId);

        Assert.Collection(
            conversation.Messages,
            message => Assert.Equal("First question", message.Content),
            message => Assert.Equal("First answer", message.Content),
            message => Assert.Same(edited, message));
        Assert.DoesNotContain(
            conversation.Messages,
            message => message.RevisionId == laterParentMessage.RevisionId);
        Assert.Equal(5, conversation.GetMessages(originalBranchId).Count);
    }

    [Fact]
    public void EditUserMessageRejectsAssistantAndNonActiveRevision()
    {
        Conversation conversation = CreateAnsweredConversation();
        ChatMessage assistant = conversation.Messages[1];
        ChatMessage target = conversation.Messages[2];
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;

        Assert.Throws<InvalidOperationException>(() =>
            conversation.EditUserMessage(
                assistant.RevisionId,
                "Cannot edit assistant",
                _createdAt.AddMinutes(5)));

        conversation.EditUserMessage(
            target.RevisionId,
            "Updated",
            _createdAt.AddMinutes(5));
        ChatMessage oldDescendant = conversation.GetMessages(originalBranchId)[3];

        Assert.Throws<KeyNotFoundException>(() =>
            conversation.EditUserMessage(
                oldDescendant.RevisionId,
                "Not active",
                _createdAt.AddMinutes(6)));
    }

    [Fact]
    public void RestoreRejectsOrphanRevision()
    {
        ChatMessage orphan = new(
            MessageId.New(),
            MessageRole.User,
            "Orphan",
            _createdAt);
        ConversationBranch root = new(
            ConversationBranchId.New(),
            parentBranchId: null,
            forkedAfterRevisionId: null,
            localRevisionIds: Array.Empty<MessageRevisionId>());

        Assert.Throws<InvalidOperationException>(() =>
            Conversation.Restore(
                ConversationId.New(),
                Conversation.DefaultTitle,
                _createdAt,
                _createdAt,
                [orphan],
                [root],
                root.Id));
    }

    [Fact]
    public void RestoreRejectsTwoRevisionsOfSameMessageOnOneBranch()
    {
        MessageId messageId = MessageId.New();
        ChatMessage first = new(
            messageId,
            MessageRevisionId.New(),
            parentRevisionId: null,
            MessageRole.User,
            "First",
            _createdAt);
        ChatMessage second = new(
            messageId,
            MessageRevisionId.New(),
            first.RevisionId,
            MessageRole.User,
            "Second",
            _createdAt.AddMinutes(1));
        ConversationBranch root = new(
            ConversationBranchId.New(),
            parentBranchId: null,
            forkedAfterRevisionId: null,
            [first.RevisionId, second.RevisionId]);

        Assert.Throws<InvalidOperationException>(() =>
            Conversation.Restore(
                ConversationId.New(),
                Conversation.DefaultTitle,
                _createdAt,
                _createdAt.AddMinutes(1),
                [first, second],
                [root],
                root.Id));
    }

    [Fact]
    public void RestoreRejectsUpdatedAtBeforePersistedRevision()
    {
        ChatMessage message = new(
            MessageId.New(),
            MessageRole.User,
            "Later",
            _createdAt.AddMinutes(1));
        ConversationBranch root = new(
            ConversationBranchId.New(),
            parentBranchId: null,
            forkedAfterRevisionId: null,
            [message.RevisionId]);

        Assert.Throws<InvalidOperationException>(() =>
            Conversation.Restore(
                ConversationId.New(),
                Conversation.DefaultTitle,
                _createdAt,
                _createdAt,
                [message],
                [root],
                root.Id));
    }

    [Fact]
    public void RestoreRejectsChildThatDoesNotReplaceFirstOmittedParentMessage()
    {
        ChatMessage first = new(
            MessageId.New(),
            MessageRole.User,
            "First",
            _createdAt);
        ChatMessage second = new(
            MessageId.New(),
            MessageRole.User,
            "Second",
            _createdAt.AddMinutes(1));
        ChatMessage editedSecond = new(
            second.Id,
            MessageRevisionId.New(),
            second.RevisionId,
            MessageRole.User,
            "Edited second",
            _createdAt.AddMinutes(2));
        ConversationBranch root = new(
            ConversationBranchId.New(),
            parentBranchId: null,
            forkedAfterRevisionId: null,
            [first.RevisionId, second.RevisionId]);
        ConversationBranch child = new(
            ConversationBranchId.New(),
            root.Id,
            forkedAfterRevisionId: null,
            [editedSecond.RevisionId]);

        Assert.Throws<InvalidOperationException>(() =>
            Conversation.Restore(
                ConversationId.New(),
                Conversation.DefaultTitle,
                _createdAt,
                _createdAt.AddMinutes(2),
                [first, second, editedSecond],
                [root, child],
                child.Id));
    }

    private static Conversation CreateAnsweredConversation()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "First question",
            _createdAt.AddMinutes(1)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "First answer",
            _createdAt.AddMinutes(2)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Second question",
            _createdAt.AddMinutes(3)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Second answer",
            _createdAt.AddMinutes(4)));
        return conversation;
    }
}
