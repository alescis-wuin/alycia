using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task InitializeAsyncShowsEmptyHistoryWhenRepositoryIsEmpty()
    {
        InMemoryConversationRepository repository = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 8, 6, 0, 0, TimeSpan.Zero)));

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.IsInitialized);
        Assert.True(viewModel.IsHistoryEmpty);
        Assert.Empty(viewModel.Conversations);
        Assert.Null(viewModel.SelectedConversation);
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public async Task InitializeAsyncSelectsMostRecentlyUpdatedConversationAndProjectsMessages()
    {
        DateTimeOffset olderCreatedAt = new(2026, 8, 8, 6, 0, 0, TimeSpan.Zero);
        DateTimeOffset newerCreatedAt = olderCreatedAt.AddHours(1);
        InMemoryConversationRepository repository = new();

        Conversation older = new(ConversationId.New(), "Older", olderCreatedAt, olderCreatedAt);
        older.AddMessage(new ChatMessage(MessageId.New(), MessageRole.User, "Old message", olderCreatedAt.AddMinutes(1)));
        repository.Seed(older);

        Conversation newer = new(ConversationId.New(), "Newest", newerCreatedAt, newerCreatedAt);
        newer.AddMessage(new ChatMessage(MessageId.New(), MessageRole.Assistant, "Latest message", newerCreatedAt.AddMinutes(1)));
        repository.Seed(newer);

        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(newerCreatedAt.AddHours(1)));

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(2, viewModel.Conversations.Count);
        Assert.Equal(newer.Id, viewModel.SelectedConversation?.Id);
        MessageViewModel message = Assert.Single(viewModel.Messages);
        Assert.Equal("Alicia", message.RoleLabel);
        Assert.Equal("Latest message", message.Content);
        Assert.True(viewModel.HasMessages);
    }

    [Fact]
    public async Task CreateConversationCommandCreatesSelectsAndShowsEmptyConversation()
    {
        DateTimeOffset now = new(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(now));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        await viewModel.CreateConversationCommand.ExecuteAsync(null).ConfigureAwait(true);

        ConversationListItemViewModel item = Assert.Single(viewModel.Conversations);
        Assert.Same(item, viewModel.SelectedConversation);
        Assert.Equal(Conversation.DefaultTitle, item.Title);
        Assert.True(viewModel.IsSelectedConversationEmpty);
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public async Task InlineRenameCommandsPersistAndRefreshSelectedConversation()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);
        MutableTimeProvider timeProvider = new(createdAt.AddMinutes(10));
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        repository.Seed(conversation);
        MainViewModel viewModel = CreateViewModel(repository, timeProvider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.BeginRenameCommand.Execute(null);
        ConversationListItemViewModel item = Assert.IsType<ConversationListItemViewModel>(viewModel.SelectedConversation);
        Assert.True(item.IsRenaming);
        Assert.False(item.CanSaveRename);

        item.RenameTitle = "  Project notes  ";
        Assert.True(item.CanSaveRename);
        await item.SaveRenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal("Project notes", viewModel.SelectedConversation?.Title);
        Assert.False(viewModel.SelectedConversation?.IsRenaming);
        Conversation? persisted = await repository.FindAsync(conversation.Id, CancellationToken.None).ConfigureAwait(true);
        Conversation persistedConversation = Assert.IsType<Conversation>(persisted);
        Assert.Equal("Project notes", persistedConversation.Title);
        Assert.Equal(timeProvider.UtcNow, persistedConversation.UpdatedAt);
    }

    [Fact]
    public async Task InlineRenameRejectsBlankAndUnchangedTitlesAndCancelRestoresOriginalTitle()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 9, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(createdAt));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.BeginRenameCommand.Execute(null);
        ConversationListItemViewModel item = Assert.IsType<ConversationListItemViewModel>(viewModel.SelectedConversation);

        Assert.Equal("Project", item.RenameTitle);
        Assert.False(item.CanSaveRename);

        item.RenameTitle = "   ";
        Assert.False(item.CanSaveRename);

        item.RenameTitle = "Updated project";
        Assert.True(item.CanSaveRename);
        item.CancelRenameCommand.Execute(null);

        Assert.False(item.IsRenaming);
        Assert.True(item.IsTitleVisible);
        Assert.Equal("Project", item.RenameTitle);
    }

    [Fact]
    public async Task DeleteCommandsRemoveSelectedConversationAndSelectNextConversation()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation older = new(ConversationId.New(), "Older", createdAt, createdAt);
        Conversation newer = new(ConversationId.New(), "Newer", createdAt.AddMinutes(1), createdAt.AddMinutes(1));
        repository.Seed(older);
        repository.Seed(newer);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(2)));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(newer.Id, viewModel.SelectedConversation?.Id);
        viewModel.RequestDeleteCommand.Execute(null);
        Assert.True(viewModel.IsDeleteConfirmationVisible);
        Assert.False(viewModel.IsComposerEnabled);

        await viewModel.ConfirmDeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.False(viewModel.IsDeleteConfirmationVisible);
        ConversationListItemViewModel remaining = Assert.Single(viewModel.Conversations);
        Assert.Equal(older.Id, remaining.Id);
        Assert.Same(remaining, viewModel.SelectedConversation);
        Assert.Null(await repository.FindAsync(newer.Id, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task HistoryRenameCommandSelectsTargetBeforeOpeningInlineEditor()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation older = new(ConversationId.New(), "Older", createdAt, createdAt);
        Conversation newer = new(ConversationId.New(), "Newer", createdAt.AddMinutes(1), createdAt.AddMinutes(1));
        repository.Seed(older);
        repository.Seed(newer);
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(createdAt.AddMinutes(2)));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        ConversationListItemViewModel olderItem = Assert.Single(
            viewModel.Conversations,
            item => item.Id == older.Id);
        ConversationListItemViewModel newerItem = Assert.Single(
            viewModel.Conversations,
            item => item.Id == newer.Id);

        await olderItem.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(older.Id, viewModel.SelectedConversation?.Id);
        Assert.True(olderItem.IsRenaming);
        Assert.False(olderItem.IsTitleVisible);
        Assert.False(newerItem.IsRenaming);
        Assert.False(viewModel.IsDeleteConfirmationVisible);
        Assert.Equal("Older", olderItem.RenameTitle);
    }

    [Fact]
    public async Task StartingInlineRenameOnAnotherConversationClosesPreviousEditor()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 12, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation older = new(ConversationId.New(), "Older", createdAt, createdAt);
        Conversation newer = new(ConversationId.New(), "Newer", createdAt.AddMinutes(1), createdAt.AddMinutes(1));
        repository.Seed(older);
        repository.Seed(newer);
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(createdAt.AddMinutes(2)));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        ConversationListItemViewModel olderItem = Assert.Single(viewModel.Conversations, item => item.Id == older.Id);
        ConversationListItemViewModel newerItem = Assert.Single(viewModel.Conversations, item => item.Id == newer.Id);

        await olderItem.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(olderItem.IsRenaming);

        await newerItem.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.False(olderItem.IsRenaming);
        Assert.True(newerItem.IsRenaming);
        Assert.Equal(newer.Id, viewModel.SelectedConversation?.Id);
    }

    [Fact]
    public async Task HistoryDeleteCommandSelectsTargetAndClosesInlineRenameBeforeShowingConfirmation()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 13, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation older = new(ConversationId.New(), "Older", createdAt, createdAt);
        Conversation newer = new(ConversationId.New(), "Newer", createdAt.AddMinutes(1), createdAt.AddMinutes(1));
        repository.Seed(older);
        repository.Seed(newer);
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(createdAt.AddMinutes(2)));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        ConversationListItemViewModel olderItem = Assert.Single(viewModel.Conversations, item => item.Id == older.Id);
        await olderItem.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(olderItem.IsRenaming);

        await olderItem.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(older.Id, viewModel.SelectedConversation?.Id);
        Assert.False(olderItem.IsRenaming);
        Assert.True(viewModel.IsDeleteConfirmationVisible);
        Assert.Contains("Older", viewModel.DeletePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelTransientActionClosesInlineRenameAndDeleteStates()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        repository.Seed(new Conversation(ConversationId.New(), "Project", createdAt, createdAt));
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(createdAt));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.BeginRenameCommand.Execute(null);
        ConversationListItemViewModel item = Assert.IsType<ConversationListItemViewModel>(viewModel.SelectedConversation);
        Assert.True(item.IsRenaming);

        viewModel.CancelTransientActionCommand.Execute(null);
        Assert.False(item.IsRenaming);

        viewModel.RequestDeleteCommand.Execute(null);
        Assert.True(viewModel.IsDeleteConfirmationVisible);
        Assert.False(viewModel.IsComposerEnabled);
        viewModel.CancelTransientActionCommand.Execute(null);

        Assert.False(item.IsRenaming);
        Assert.False(viewModel.IsDeleteConfirmationVisible);
        Assert.True(viewModel.IsComposerEnabled);
        Assert.Equal("Project", item.RenameTitle);
    }

    [Fact]
    public void ConversationListItemProjectsAccessibleActionLabelsToolTipsAndMetadata()
    {
        DateTimeOffset updatedAt = new(2026, 8, 8, 15, 0, 0, TimeSpan.Zero);
        ConversationSummary summary = new(
            ConversationId.New(),
            "Project notes",
            updatedAt.AddHours(-1),
            updatedAt,
            2);
        ConversationListItemViewModel viewModel = new(
            summary,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => true);

        Assert.Equal("2 messages", viewModel.MessageCountLabel);
        Assert.Contains("2 messages", viewModel.MetadataLabel, StringComparison.Ordinal);
        Assert.Equal("Open conversation Project notes", viewModel.SelectionAutomationName);
        Assert.Equal("Rename conversation Project notes", viewModel.RenameAutomationName);
        Assert.Equal("Delete conversation Project notes", viewModel.DeleteAutomationName);
        Assert.Equal("New title for conversation Project notes", viewModel.RenameInputAutomationName);
        Assert.Equal("Save new title for conversation Project notes", viewModel.SaveRenameAutomationName);
        Assert.Equal("Cancel renaming conversation Project notes", viewModel.CancelRenameAutomationName);
        Assert.Contains("Project notes", viewModel.SelectionToolTip, StringComparison.Ordinal);
        Assert.Contains("Project notes", viewModel.RenameToolTip, StringComparison.Ordinal);
        Assert.Contains("Project notes", viewModel.DeleteToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageCommandProjectsStreamingAssistantBeforeFinalPersistence()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 17, 0, 0, TimeSpan.Zero);
        MutableTimeProvider timeProvider = new(createdAt.AddMinutes(2));
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        PausingStreamingConversationResponder responder = new(
            "Partial ",
            "response");
        MainViewModel viewModel = CreateViewModel(
            repository,
            timeProvider,
            responder);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Stream this";

        Task sendTask = viewModel.SendMessageCommand.ExecuteAsync(null);
        await responder.FirstChunkObserved.ConfigureAwait(true);

        Assert.True(viewModel.IsGeneratingResponse);
        Assert.True(viewModel.CanStopResponse);
        Assert.Collection(
            viewModel.Messages,
            message =>
            {
                Assert.True(message.IsUser);
                Assert.Equal("Stream this", message.Content);
            },
            message =>
            {
                Assert.True(message.IsAssistant);
                Assert.True(message.IsStreaming);
                Assert.Equal("Partial ", message.Content);
                Assert.Equal("Streaming…", message.CreatedAtLabel);
            });

        Conversation? duringStream = await repository
            .FindAsync(conversation.Id, CancellationToken.None)
            .ConfigureAwait(true);
        Conversation persistedDuringStream = Assert.IsType<Conversation>(duringStream);
        ChatMessage persistedUser = Assert.Single(persistedDuringStream.Messages);
        Assert.Equal(MessageRole.User, persistedUser.Role);

        responder.Release();
        await sendTask.ConfigureAwait(true);

        Assert.False(viewModel.IsGeneratingResponse);
        Assert.False(viewModel.CanRetryResponse);
        Assert.Collection(
            viewModel.Messages,
            message => Assert.True(message.IsUser),
            message =>
            {
                Assert.True(message.IsAssistant);
                Assert.False(message.IsStreaming);
                Assert.Equal("Partial response", message.Content);
            });
        Assert.Collection(
            persistedDuringStream.Messages,
            message => Assert.Equal(MessageRole.User, message.Role),
            message =>
            {
                Assert.Equal(MessageRole.Assistant, message.Role);
                Assert.Equal("Partial response", message.Content);
            });
    }

    [Fact]
    public async Task SendMessageCommandPersistsCompleteTurnAndRefreshesProjection()
    {
        DateTimeOffset createdAt = new(2026, 8, 10, 20, 0, 0, TimeSpan.Zero);
        MutableTimeProvider timeProvider = new(createdAt.AddMinutes(5));
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        DeterministicConversationResponder responder =
            new("Development response");
        MainViewModel viewModel = CreateViewModel(
            repository,
            timeProvider,
            responder);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.MessageDraft = "  First line\nSecond line  ";

        Assert.True(viewModel.CanSendMessage);
        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(string.Empty, viewModel.MessageDraft);
        Assert.False(viewModel.CanSendMessage);
        Assert.False(viewModel.IsSendingMessage);
        Assert.False(viewModel.IsGeneratingResponse);
        Assert.False(viewModel.CanRetryResponse);
        Assert.Equal(1, responder.CallCount);

        Assert.Collection(
            viewModel.Messages,
            message =>
            {
                Assert.Equal("You", message.RoleLabel);
                Assert.Equal("First line\nSecond line", message.Content);
                Assert.True(message.IsUser);
            },
            message =>
            {
                Assert.Equal("Alicia", message.RoleLabel);
                Assert.Equal("Development response", message.Content);
                Assert.True(message.IsAssistant);
            });

        ConversationListItemViewModel item = Assert.Single(viewModel.Conversations);
        Assert.Equal(conversation.Id, item.Id);
        Assert.Equal(2, item.MessageCount);
        Assert.Contains("2 messages", item.MetadataLabel, StringComparison.Ordinal);

        Conversation? persisted = await repository
            .FindAsync(conversation.Id, CancellationToken.None)
            .ConfigureAwait(true);
        Conversation persistedConversation = Assert.IsType<Conversation>(persisted);
        Assert.Collection(
            persistedConversation.Messages,
            message =>
            {
                Assert.Equal(MessageRole.User, message.Role);
                Assert.Equal("First line\nSecond line", message.Content);
            },
            message =>
            {
                Assert.Equal(MessageRole.Assistant, message.Role);
                Assert.Equal("Development response", message.Content);
            });
        Assert.Equal(timeProvider.UtcNow, persistedConversation.UpdatedAt);
    }

    [Fact]
    public async Task SendMessageCommandRequiresSelectionAndMeaningfulDraft()
    {
        DateTimeOffset now = new(2026, 8, 10, 20, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(now));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.MessageDraft = "Hello";
        Assert.False(viewModel.CanSendMessage);
        Assert.False(viewModel.SendMessageCommand.CanExecute(null));

        await viewModel.CreateConversationCommand.ExecuteAsync(null).ConfigureAwait(true);

        viewModel.MessageDraft = "   ";
        Assert.False(viewModel.CanSendMessage);
        Assert.False(viewModel.SendMessageCommand.CanExecute(null));

        viewModel.MessageDraft = "Hello";
        Assert.True(viewModel.CanSendMessage);
        Assert.True(viewModel.SendMessageCommand.CanExecute(null));
    }

    [Fact]
    public async Task SendMessageFailureKeepsDraftAvailableForRetry()
    {
        DateTimeOffset createdAt = new(2026, 8, 10, 20, 45, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        repository.SaveException = new IOException("Local storage is unavailable.");
        viewModel.MessageDraft = "Retry this message";

        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal("Retry this message", viewModel.MessageDraft);
        Assert.True(viewModel.HasError);
        string errorMessage = Assert.IsType<string>(viewModel.ErrorMessage);
        Assert.Contains("Local storage is unavailable", errorMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.Messages);
        Assert.True(viewModel.CanSendMessage);
        Assert.False(viewModel.IsSendingMessage);
    }

    [Fact]
    public async Task SelectingAnotherConversationClearsDraftToPreventCrossConversationSend()
    {
        DateTimeOffset createdAt = new(2026, 8, 10, 21, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation older = new(ConversationId.New(), "Older", createdAt, createdAt);
        Conversation newer = new(
            ConversationId.New(),
            "Newer",
            createdAt.AddMinutes(1),
            createdAt.AddMinutes(1));
        repository.Seed(older);
        repository.Seed(newer);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(2)));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.MessageDraft = "Draft for newer";
        ConversationListItemViewModel olderItem = Assert.Single(
            viewModel.Conversations,
            item => item.Id == older.Id);

        await olderItem.SelectCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(older.Id, viewModel.SelectedConversation?.Id);
        Assert.Equal(string.Empty, viewModel.MessageDraft);
        Assert.False(viewModel.CanSendMessage);
    }

    [Fact]
    public async Task ReloadingSameConversationPreservesDraft()
    {
        DateTimeOffset createdAt = new(2026, 8, 10, 21, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        MainViewModel viewModel = CreateViewModel(repository, new MutableTimeProvider(createdAt));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.MessageDraft = "Keep this draft";
        await viewModel.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(conversation.Id, viewModel.SelectedConversation?.Id);
        Assert.Equal("Keep this draft", viewModel.MessageDraft);
        Assert.True(viewModel.CanSendMessage);
    }

    [Fact]
    public async Task StopResponseDiscardsPartialProjectionKeepsUserMessageAndEnablesRetry()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 30, 0, TimeSpan.Zero);
        MutableTimeProvider timeProvider = new(createdAt.AddMinutes(1));
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        PausingStreamingConversationResponder responder = new(
            "Partial response",
            " that must not persist");
        MainViewModel viewModel = CreateViewModel(
            repository,
            timeProvider,
            responder);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Stop this";

        Task sendTask = viewModel.SendMessageCommand.ExecuteAsync(null);
        await responder.FirstChunkObserved.ConfigureAwait(true);

        Assert.True(viewModel.IsGeneratingResponse);
        Assert.True(viewModel.CanStopResponse);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsComposerEnabled);
        Assert.Equal(2, viewModel.Messages.Count);
        Assert.True(viewModel.Messages[1].IsStreaming);
        Assert.Equal("Partial response", viewModel.Messages[1].Content);

        viewModel.StopResponseCommand.Execute(null);
        await sendTask.ConfigureAwait(true);

        Assert.False(viewModel.IsGeneratingResponse);
        Assert.False(viewModel.CanStopResponse);
        Assert.True(viewModel.CanRetryResponse);
        Assert.False(viewModel.HasError);
        MessageViewModel userMessage = Assert.Single(viewModel.Messages);
        Assert.True(userMessage.IsUser);
        Assert.Equal("Stop this", userMessage.Content);

        Conversation? persisted = await repository
            .FindAsync(conversation.Id, CancellationToken.None)
            .ConfigureAwait(true);
        Conversation persistedConversation = Assert.IsType<Conversation>(persisted);
        ChatMessage persistedMessage = Assert.Single(persistedConversation.Messages);
        Assert.Equal(MessageRole.User, persistedMessage.Role);
    }

    [Fact]
    public async Task RetryResponseCompletesExistingUserMessageWithoutDuplicatingIt()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 40, 0, TimeSpan.Zero);
        MutableTimeProvider timeProvider = new(createdAt.AddMinutes(1));
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        FailOnceConversationResponder responder =
            new("Recovered response");
        MainViewModel viewModel = CreateViewModel(
            repository,
            timeProvider,
            responder);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Retry me";

        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.CanRetryResponse);
        Assert.Equal(1, responder.CallCount);
        MessageViewModel firstProjection = Assert.Single(viewModel.Messages);
        Assert.True(firstProjection.IsUser);

        await viewModel.RetryResponseCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.False(viewModel.HasError);
        Assert.False(viewModel.CanRetryResponse);
        Assert.Equal(2, responder.CallCount);
        Assert.Collection(
            viewModel.Messages,
            message =>
            {
                Assert.True(message.IsUser);
                Assert.Equal("Retry me", message.Content);
            },
            message =>
            {
                Assert.True(message.IsAssistant);
                Assert.Equal("Recovered response", message.Content);
            });

        Conversation? persisted = await repository
            .FindAsync(conversation.Id, CancellationToken.None)
            .ConfigureAwait(true);
        Conversation persistedConversation = Assert.IsType<Conversation>(persisted);
        Assert.Equal(
            1,
            persistedConversation.Messages.Count(
                message => message.Role == MessageRole.User));
        Assert.Equal(
            1,
            persistedConversation.Messages.Count(
                message => message.Role == MessageRole.Assistant));
    }

    [Fact]
    public async Task EscapeCancellationStopsActiveResponse()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 50, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        CancellableConversationResponder responder = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            responder);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Escape";

        Task sendTask = viewModel.SendMessageCommand.ExecuteAsync(null);
        await responder.Started.ConfigureAwait(true);

        viewModel.CancelTransientActionCommand.Execute(null);
        await sendTask.ConfigureAwait(true);

        Assert.False(viewModel.IsGeneratingResponse);
        Assert.True(viewModel.CanRetryResponse);
        Assert.False(viewModel.HasError);
    }

    [Theory]
    [InlineData(MessageRole.System, "System")]
    [InlineData(MessageRole.User, "You")]
    [InlineData(MessageRole.Assistant, "Alicia")]
    public void MessageViewModelProjectsRoleLabel(MessageRole role, string expectedLabel)
    {
        ChatMessage message = new(
            MessageId.New(),
            role,
            "Message",
            new DateTimeOffset(2026, 8, 8, 11, 0, 0, TimeSpan.Zero));

        MessageViewModel viewModel = new(message);

        Assert.Equal(expectedLabel, viewModel.RoleLabel);
        Assert.Equal("Message", viewModel.Content);
        Assert.Equal(role == MessageRole.System, viewModel.IsSystem);
        Assert.Equal(role == MessageRole.User, viewModel.IsUser);
        Assert.Equal(role == MessageRole.Assistant, viewModel.IsAssistant);
        Assert.False(viewModel.IsStreaming);
        Assert.Contains(expectedLabel, viewModel.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamingMessageViewModelAppendsDeltasWithoutCreatingDomainMessage()
    {
        MessageViewModel viewModel = MessageViewModel.CreateStreamingAssistant();

        viewModel.AppendContentDelta("Hello");
        viewModel.AppendContentDelta(" world");

        Assert.True(viewModel.IsAssistant);
        Assert.True(viewModel.IsStreaming);
        Assert.Equal("Alicia", viewModel.RoleLabel);
        Assert.Equal("Hello world", viewModel.Content);
        Assert.Equal("Streaming…", viewModel.CreatedAtLabel);
        Assert.Contains("in progress", viewModel.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsyncDetectsProviderAndRestoresModelReference()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Running);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 0, 0, TimeSpan.Zero)),
            inferenceProvider: provider);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(1, provider.DetectCount);
        Assert.True(viewModel.IsProviderRunning);
        Assert.Equal("Running", viewModel.ProviderStatusText);
        Assert.Equal("owner/model-GGUF:Q4_K_M", viewModel.ProviderModelReference);
        Assert.False(viewModel.CanInstallProvider);
    }

    [Fact]
    public async Task MissingProviderCanBeInstalledWithoutTerminalCommands()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Missing);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 10, 0, TimeSpan.Zero)),
            inferenceProvider: provider);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.CanInstallProvider);
        Assert.False(viewModel.IsProviderRunning);

        await viewModel.InstallProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.InstallCount);
        Assert.Equal("Ready", viewModel.ProviderStatusText);
        Assert.False(viewModel.CanInstallProvider);
        Assert.False(viewModel.IsProviderRunning);
    }

    [Fact]
    public async Task ProviderInstallationSurfacesStructuredProgress()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Missing);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 15, 0, TimeSpan.Zero)),
            inferenceProvider: provider);

        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.InstallProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.IsProviderProgressVisible);
        Assert.False(viewModel.IsProviderProgressIndeterminate);
        Assert.Equal(100d, viewModel.ProviderProgressValue);
        Assert.Contains("Installation complete", viewModel.ProviderProgressText, StringComparison.Ordinal);
        Assert.Equal("Test provider installed.", viewModel.ProviderProgressDetailText);
    }

    [Fact]
    public async Task StartProviderPassesHuggingFaceModelReferenceAndEnablesChat()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 20, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.ProviderModelReference = "ggml-org/gemma-3-1b-it-GGUF:Q4_K_M";

        Assert.True(viewModel.HasProviderConfigurationChanges);
        Assert.False(viewModel.CanStartProvider);
        Assert.True(viewModel.CanSaveProviderConfiguration);

        await viewModel.SaveProviderConfigurationCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.CanStartProvider);
        await viewModel.StartProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.StartCount);
        Assert.Equal(
            "ggml-org/gemma-3-1b-it-GGUF:Q4_K_M",
            provider.LastStartedModel);
        Assert.True(viewModel.IsProviderRunning);
        Assert.Equal("Running", viewModel.ProviderStatusText);
    }

    [Fact]
    public async Task FailedModelStartReturnsToReadyAndCanRetryWithoutReinstalling()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            StartException = new InvalidOperationException("Model could not be loaded."),
        };
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 25, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.ProviderModelReference = "owner/model-GGUF:Q4_K_M";
        viewModel.ProviderTemperatureText = "0.6";
        await viewModel.SaveProviderConfigurationCommand.ExecuteAsync(null).ConfigureAwait(true);

        await viewModel.StartProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.HasError);
        Assert.Equal("Ready", viewModel.ProviderStatusText);
        Assert.True(viewModel.CanStartProvider);
        Assert.False(viewModel.CanInstallProvider);
        Assert.Equal(2, provider.DetectCount);
    }

    [Fact]
    public async Task MissingProviderPreventsSendingUntilLocalAiIsRunning()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 21, 27, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        repository.Seed(new Conversation(
            ConversationId.New(),
            "Provider gate",
            createdAt,
            createdAt));
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Missing);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Hello";

        Assert.True(viewModel.IsComposerEnabled);
        Assert.False(viewModel.CanSendMessage);
        Assert.Contains("Start the selected provider", viewModel.ComposerStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshProviderConfigurationRequiresExplicitSaveBeforeModelStart()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        StubInferenceProviderConfigurationStore configurationStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 28, 0, TimeSpan.Zero)),
            inferenceProvider: provider,
            providerConfigurationStore: configurationStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.ProviderModelReference = "owner/model-GGUF:Q4_K_M";

        Assert.True(viewModel.HasProviderConfigurationChanges);
        Assert.True(viewModel.CanSaveProviderConfiguration);
        Assert.False(viewModel.CanStartProvider);
        Assert.Contains("Fallback disabled", viewModel.ProviderFallbackText, StringComparison.Ordinal);

        await viewModel.SaveProviderConfigurationCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, configurationStore.SaveCount);
        Assert.False(viewModel.HasProviderConfigurationChanges);
        Assert.True(viewModel.CanStartProvider);
    }

    [Fact]
    public async Task SavedGenerationSettingsAreValidatedAndPassedToProvider()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        StubInferenceProviderConfigurationStore configurationStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 29, 0, TimeSpan.Zero)),
            inferenceProvider: provider,
            providerConfigurationStore: configurationStore);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.ProviderModelReference = "owner/model-GGUF:Q5_K_M";
        viewModel.ProviderContextSizeText = "8192";
        viewModel.ProviderMaxOutputTokensText = "256";
        viewModel.ProviderTemperatureText = "0.6";
        viewModel.ProviderTopPText = "0.9";
        viewModel.ProviderTopKText = "50";
        viewModel.ProviderSeedText = "42";

        Assert.False(viewModel.HasProviderConfigurationValidationError);
        await viewModel.SaveProviderConfigurationCommand.ExecuteAsync(null).ConfigureAwait(true);
        await viewModel.StartProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        InferenceProviderConfiguration saved = Assert.IsType<InferenceProviderConfiguration>(
            configurationStore.LastSavedConfiguration);
        Assert.Equal("llama.cpp.cuda", saved.ProviderId);
        Assert.Equal("owner/model-GGUF:Q5_K_M", saved.ModelReference);
        Assert.Equal(8192, saved.ContextSize);
        Assert.Equal(256, saved.Generation.MaxOutputTokens);
        Assert.Equal(0.6, saved.Generation.Temperature);
        Assert.Equal(0.9, saved.Generation.TopP);
        Assert.Equal(50, saved.Generation.TopK);
        Assert.Equal(42, saved.Generation.Seed);
        Assert.Equal(saved, provider.LastStartedConfiguration);
    }

    [Fact]
    public async Task InvalidGenerationSettingBlocksSaveAndStart()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 29, 30, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.ProviderTopPText = "1.5";

        Assert.True(viewModel.HasProviderConfigurationValidationError);
        Assert.Contains("Top-p", viewModel.ProviderConfigurationValidationText, StringComparison.Ordinal);
        Assert.False(viewModel.CanSaveProviderConfiguration);
        Assert.False(viewModel.CanStartProvider);
    }

    [Fact]
    public async Task BlankOptionalSettingsPreserveProviderDefaults()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        StubInferenceProviderConfigurationStore configurationStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 29, 45, TimeSpan.Zero)),
            inferenceProvider: provider,
            providerConfigurationStore: configurationStore);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.ProviderModelReference = "owner/model-GGUF";
        await viewModel.SaveProviderConfigurationCommand.ExecuteAsync(null).ConfigureAwait(true);

        InferenceProviderConfiguration saved = Assert.IsType<InferenceProviderConfiguration>(
            configurationStore.LastSavedConfiguration);
        Assert.Null(saved.ContextSize);
        Assert.True(saved.Generation.UsesOnlyProviderDefaults);
        Assert.True(saved.UsesProviderDefaults);
        Assert.Contains("provider defaults", viewModel.ProviderConfigurationStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StopProviderReturnsRuntimeToReadyState()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Running);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 30, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.CanStopProvider);
        await viewModel.StopProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.StopCount);
        Assert.False(viewModel.IsProviderRunning);
        Assert.Equal("Ready", viewModel.ProviderStatusText);
    }

    private static MainViewModel CreateViewModel(
        IConversationRepository repository,
        TimeProvider timeProvider,
        IStreamingConversationResponder? responder = null,
        IInferenceProviderRuntime? inferenceProvider = null,
        IInferenceProviderRegistry? providerRegistry = null,
        IInferenceProviderConfigurationStore? providerConfigurationStore = null)
    {
        IStreamingConversationResponder resolvedResponder =
            responder ?? new DeterministicConversationResponder("Development response");
        IInferenceProviderRuntime resolvedProvider =
            inferenceProvider ?? new StubInferenceProviderRuntime();
        IInferenceProviderRegistry resolvedRegistry =
            providerRegistry ?? new StubInferenceProviderRegistry(resolvedProvider);
        IInferenceProviderConfigurationStore resolvedConfigurationStore =
            providerConfigurationStore
            ?? new StubInferenceProviderConfigurationStore(
                new InferenceProviderConfiguration(
                    "llama.cpp.cuda",
                    "owner/model-GGUF:Q4_K_M"));

        return new MainViewModel(
            new CreateConversationUseCase(repository, timeProvider),
            new AppendMessageUseCase(repository, timeProvider),
            new StreamConversationTurnUseCase(
                repository,
                resolvedResponder,
                timeProvider),
            new LoadConversationUseCase(repository),
            new ListConversationsUseCase(repository),
            new RenameConversationUseCase(repository, timeProvider),
            new DeleteConversationUseCase(repository),
            resolvedRegistry,
            resolvedConfigurationStore);
    }
}
