using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class ConversationBranchUseCaseTests
{
    private static readonly DateTimeOffset _createdAt =
        new(2026, 9, 17, 21, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task EditMessageCreatesAndPersistsActiveChildBranch()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage original = new(
            MessageId.New(),
            MessageRole.User,
            "Original",
            _createdAt.AddMinutes(1));
        conversation.AddMessage(original);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        DateTimeOffset editedAt = _createdAt.AddMinutes(2);
        EditMessageUseCase useCase = new(repository, new FixedTimeProvider(editedAt));

        ChatMessage edited = await useCase.ExecuteAsync(
            conversation.Id,
            original.RevisionId,
            "Edited",
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(original.RevisionId, edited.ParentRevisionId);
        Assert.Equal("Edited", edited.Content);
        Assert.NotEqual(originalBranchId, conversation.ActiveBranchId);
        Assert.Same(edited, Assert.Single(conversation.Messages));
        Assert.Same(original, Assert.Single(conversation.GetMessages(originalBranchId)));
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task ActivateConversationBranchPersistsSelectionWithoutChangingUpdatedAt()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage original = new(
            MessageId.New(),
            MessageRole.User,
            "Original",
            _createdAt.AddMinutes(1));
        conversation.AddMessage(original);
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        conversation.EditUserMessage(
            original.RevisionId,
            "Edited",
            _createdAt.AddMinutes(2));
        DateTimeOffset updatedAt = conversation.UpdatedAt;
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        ActivateConversationBranchUseCase useCase = new(repository);

        Conversation selected = await useCase.ExecuteAsync(
            conversation.Id,
            originalBranchId,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Same(conversation, selected);
        Assert.Equal(originalBranchId, selected.ActiveBranchId);
        Assert.Equal("Original", Assert.Single(selected.Messages).Content);
        Assert.Equal(updatedAt, selected.UpdatedAt);
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task EditMessageRejectsUnknownConversation()
    {
        InMemoryConversationRepository repository = new();
        EditMessageUseCase useCase = new(
            repository,
            new FixedTimeProvider(_createdAt));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(
                ConversationId.New(),
                MessageRevisionId.New(),
                "Edited",
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task CompleteTurnRejectsBranchSwitchWhileGenerationIsInFlight()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage user = new(
            MessageId.New(),
            MessageRole.User,
            "Original",
            _createdAt.AddMinutes(1));
        conversation.AddMessage(user);
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        ChatMessage edited = conversation.EditUserMessage(
            user.RevisionId,
            "Edited",
            _createdAt.AddMinutes(2));
        DateTimeOffset updatedAt = conversation.UpdatedAt;
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);

        DelegateConversationResponder responder = new(async (_, cancellationToken) =>
        {
            conversation.ActivateBranch(originalBranchId);
            Assert.Equal(updatedAt, conversation.UpdatedAt);
            await repository.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
            return new ConversationResponse("Stale");
        });
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(_createdAt.AddMinutes(3)));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(
                conversation.Id,
                edited.Id,
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "changed while Alicia was responding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(originalBranchId, conversation.ActiveBranchId);
        Assert.Single(conversation.Messages);
        Assert.Equal("Original", conversation.Messages[0].Content);
    }

    [Fact]
    public async Task CompleteTurnUsesOnlyActiveBranchAndPreservesOriginalBranch()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage firstUser = new(
            MessageId.New(),
            MessageRole.User,
            "First",
            _createdAt.AddMinutes(1));
        ChatMessage firstAssistant = new(
            MessageId.New(),
            MessageRole.Assistant,
            "Answer",
            _createdAt.AddMinutes(2));
        ChatMessage secondUser = new(
            MessageId.New(),
            MessageRole.User,
            "Old second",
            _createdAt.AddMinutes(3));
        conversation.AddMessage(firstUser);
        conversation.AddMessage(firstAssistant);
        conversation.AddMessage(secondUser);
        ConversationBranchId originalBranchId = conversation.ActiveBranchId;
        ChatMessage edited = conversation.EditUserMessage(
            secondUser.RevisionId,
            "New second",
            _createdAt.AddMinutes(4));
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        DeterministicConversationResponder responder = new("New answer");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(_createdAt.AddMinutes(5)));

        ChatMessage assistant = await useCase.ExecuteAsync(
            conversation.Id,
            edited.Id,
            CancellationToken.None).ConfigureAwait(true);

        ConversationResponseRequest request = Assert.IsType<ConversationResponseRequest>(responder.LastRequest);
        Assert.Collection(
            request.Messages,
            message => Assert.Same(firstUser, message),
            message => Assert.Same(firstAssistant, message),
            message => Assert.Same(edited, message));
        Assert.DoesNotContain(request.Messages, message => message.RevisionId == secondUser.RevisionId);
        Assert.Same(assistant, conversation.Messages[^1]);
        Assert.Collection(
            conversation.GetMessages(originalBranchId),
            message => Assert.Same(firstUser, message),
            message => Assert.Same(firstAssistant, message),
            message => Assert.Same(secondUser, message));
    }
}
