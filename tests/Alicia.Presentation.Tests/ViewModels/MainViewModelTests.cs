using Alicia.Application.Conversations;
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
        viewModel.CancelTransientActionCommand.Execute(null);

        Assert.False(item.IsRenaming);
        Assert.False(viewModel.IsDeleteConfirmationVisible);
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
    }

    private static MainViewModel CreateViewModel(
        IConversationRepository repository,
        TimeProvider timeProvider)
    {
        return new MainViewModel(
            new CreateConversationUseCase(repository, timeProvider),
            new LoadConversationUseCase(repository),
            new ListConversationsUseCase(repository),
            new RenameConversationUseCase(repository, timeProvider),
            new DeleteConversationUseCase(repository));
    }
}
