using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.State;
using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void MainViewModelComposesDedicatedPresentationSlices()
    {
        InMemoryConversationRepository repository = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 13, 2, 0, 0, TimeSpan.Zero)));

        Assert.Same(viewModel.ConversationHistory.Conversations, viewModel.Conversations);
        Assert.Same(viewModel.ConversationStream.Messages, viewModel.Messages);
        Assert.Same(viewModel.GenerationSettings, viewModel.Model.GenerationSettings);
        Assert.NotNull(viewModel.ConversationWorkspace);
        Assert.NotNull(viewModel.Provider);

        viewModel.MessageDraft = "slice-owned draft";
        viewModel.ProviderTemperatureText = "0.35";

        Assert.Equal("slice-owned draft", viewModel.ConversationWorkspace.MessageDraft);
        Assert.Equal("0.35", viewModel.GenerationSettings.ProviderTemperatureText);
    }

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
    public async Task InitializeAsyncProjectsProfilesAndBranchScopedSelection()
    {
        DateTimeOffset now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Profile branch", now, now);
        repository.Seed(conversation);

        GenerationProfileModelScope scope = new(
            "llama.cpp.cuda",
            "owner/model-GGUF:Q4_K_M");
        GenerationProfile defaultProfile = GenerationProfile.CreateDefault(GenerationProfileId.New());
        GenerationProfile codeProfile = new(GenerationProfileId.New(), "Code");
        GenerationProfileCatalog catalog = new(
            scope,
            defaultProfile,
            [new GenerationProfileRevision(
                GenerationProfileRevisionId.New(),
                null,
                now,
                codeProfile)]);
        InMemoryGenerationProfileCatalogStore profileStore = new();
        profileStore.Seed(catalog);
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        selectionStore.Seed(new ConversationGenerationSelection(
            conversation.Id,
            conversation.ActiveBranchId,
            scope,
            codeProfile.Id));

        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(now),
            generationProfileCatalogStore: profileStore,
            conversationGenerationSelectionStore: selectionStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(2, viewModel.GenerationProfiles.Count);
        Assert.Equal(codeProfile.Id, viewModel.SelectedGenerationProfile?.Id);
        Assert.Equal(
            "llama.cpp.cuda • owner/model-GGUF:Q4_K_M",
            viewModel.GenerationProfileScopeText);
        Assert.Equal(
            "Current branch uses profile 'Code'.",
            viewModel.GenerationProfileSelectionStatusText);
        Assert.False(viewModel.CanSaveGenerationProfileSelection);

        viewModel.SelectedGenerationProfile = defaultProfile;

        Assert.True(viewModel.CanSaveGenerationProfileSelection);
        Assert.Equal(
            "Profile selection has unsaved changes for the active branch.",
            viewModel.GenerationProfileSelectionStatusText);
    }

    [Fact]
    public async Task SavingDefaultProfileCreatesCatalogAndPinsActiveBranchExplicitly()
    {
        DateTimeOffset now = new(2026, 9, 18, 10, 10, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Explicit default", now, now);
        repository.Seed(conversation);
        InMemoryGenerationProfileCatalogStore profileStore = new();
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(now),
            generationProfileCatalogStore: profileStore,
            conversationGenerationSelectionStore: selectionStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        GenerationProfile defaultProfile = Assert.Single(viewModel.GenerationProfiles);
        Assert.True(defaultProfile.IsDefault);
        Assert.Null(viewModel.SelectedGenerationProfile);

        viewModel.SelectedGenerationProfile = defaultProfile;
        Assert.True(viewModel.CanSaveGenerationProfileSelection);
        await viewModel.SaveGenerationProfileSelectionCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, profileStore.SaveCount);
        Assert.Equal(1, selectionStore.SaveCount);
        Assert.Equal(defaultProfile.Id, profileStore.LastSavedCatalog?.DefaultProfile.Id);
        ConversationGenerationSelection saved = Assert.IsType<ConversationGenerationSelection>(
            selectionStore.LastSavedSelection);
        Assert.Equal(conversation.Id, saved.ConversationId);
        Assert.Equal(conversation.ActiveBranchId, saved.BranchId);
        Assert.Equal(defaultProfile.Id, saved.ProfileId);
        Assert.Equal(
            "Current branch uses profile 'Default'.",
            viewModel.GenerationProfileSelectionStatusText);
        Assert.False(viewModel.CanSaveGenerationProfileSelection);
    }

    [Fact]
    public async Task LegacyProfileFallbackCanBePinnedToCurrentBranchWithoutChangingProfile()
    {
        DateTimeOffset now = new(2026, 9, 18, 10, 20, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Legacy profile", now, now);
        repository.Seed(conversation);
        GenerationProfileModelScope scope = new(
            "llama.cpp.cuda",
            "owner/model-GGUF:Q4_K_M");
        GenerationProfile defaultProfile = GenerationProfile.CreateDefault(GenerationProfileId.New());
        InMemoryGenerationProfileCatalogStore profileStore = new();
        profileStore.Seed(new GenerationProfileCatalog(scope, defaultProfile));
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        selectionStore.Seed(new ConversationGenerationSelection(
            conversation.Id,
            scope,
            defaultProfile.Id));
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(now),
            generationProfileCatalogStore: profileStore,
            conversationGenerationSelectionStore: selectionStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(defaultProfile.Id, viewModel.SelectedGenerationProfile?.Id);
        Assert.Contains("Conversation fallback", viewModel.GenerationProfileSelectionStatusText);
        Assert.True(viewModel.CanSaveGenerationProfileSelection);

        await viewModel.SaveGenerationProfileSelectionCommand.ExecuteAsync(null).ConfigureAwait(true);

        ConversationGenerationSelection saved = Assert.IsType<ConversationGenerationSelection>(
            selectionStore.LastSavedSelection);
        Assert.Equal(conversation.ActiveBranchId, saved.BranchId);
        Assert.Equal(defaultProfile.Id, saved.ProfileId);
        Assert.False(viewModel.CanSaveGenerationProfileSelection);
    }

    [Fact]
    public async Task UnsavedModelDraftDisablesBranchProfileSelection()
    {
        DateTimeOffset now = new(2026, 9, 18, 10, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Draft model", now, now);
        repository.Seed(conversation);
        GenerationProfileModelScope scope = new(
            "llama.cpp.cuda",
            "owner/model-GGUF:Q4_K_M");
        InMemoryGenerationProfileCatalogStore profileStore = new();
        profileStore.Seed(GenerationProfileCatalog.CreateEmpty(scope, GenerationProfileId.New()));
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(now),
            generationProfileCatalogStore: profileStore,
            conversationGenerationSelectionStore: selectionStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.ProviderModelReference = "owner/other-model-GGUF:Q8_0";

        Assert.False(viewModel.IsGenerationProfileSelectionEditable);
        Assert.False(viewModel.CanSaveGenerationProfileSelection);
        Assert.Equal(
            "Save the model/provider settings before changing the profile bound to this branch.",
            viewModel.GenerationProfileSelectionStatusText);
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
    public async Task DeleteConversationRemovesPersistedVisualIdentity()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 10, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            "Temporary",
            createdAt,
            createdAt);
        repository.Seed(conversation);
        StubConversationUiStateStore uiStateStore = new(
            ConversationUiStateSnapshot.Default.WithConversationIdentity(
                conversation.Id,
                new ConversationVisualIdentity(
                    ConversationIdentityIcon.Work,
                    ConversationIdentityColor.Blue)));
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            conversationUiStateStore: uiStateStore);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.RequestDeleteCommand.Execute(null);
        await viewModel.ConfirmDeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Empty(uiStateStore.Snapshot.ConversationIdentities);
        Assert.Equal(
            ConversationVisualIdentity.Default,
            uiStateStore.Snapshot.GetConversationIdentity(conversation.Id));
        Assert.Equal(1, uiStateStore.SaveCount);
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
        Assert.Equal(ConversationVisualIdentity.Default, viewModel.Identity);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.IdentityGlyph));
        Assert.Contains("Conversation", viewModel.IdentityDescription, StringComparison.Ordinal);
        Assert.Contains("2 messages", viewModel.AccessibilityItemStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected", viewModel.AccessibilityItemStatus, StringComparison.Ordinal);
        Assert.Contains("Conversation icon", viewModel.AccessibilityItemStatus, StringComparison.Ordinal);
        Assert.Equal(8, viewModel.IconChoices.Count);
        Assert.Equal(7, viewModel.ColorChoices.Count);
        Assert.Equal(
            "Conversation",
            Assert.Single(viewModel.IconChoices, choice => choice.IsSelected).Label);
        Assert.Equal(
            "Teal",
            Assert.Single(viewModel.ColorChoices, choice => choice.IsSelected).Label);
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
        Assert.Contains("Selected", olderItem.AccessibilityItemStatus, StringComparison.Ordinal);
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
        Assert.Equal(1, responder.CallCount);
        await Task.Delay(50, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(1, responder.CallCount);
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
    public async Task ProviderResponseFailureShowsOnlySafeMessageAndKeepsRetryAvailable()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 35, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        ProviderFailureConversationResponder responder = new(
            new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not reach the local AI server. Check the provider and try again.",
                new HttpRequestException(
                    "connection failed for /home/user/private-model.gguf token=secret")));
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Running);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            responder,
            provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Retry safely";

        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.HasError);
        string responseError = Assert.IsType<string>(viewModel.ErrorMessage);
        Assert.Equal(
            "Alicia could not reach the local AI server. Check the provider and try again.",
            responseError);
        Assert.DoesNotContain("/home/user", responseError, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", responseError, StringComparison.Ordinal);
        Assert.True(viewModel.CanRetryResponse);
        Assert.Equal(1, responder.CallCount);
        await Task.Delay(50, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(1, responder.CallCount);
        MessageViewModel userMessage = Assert.Single(viewModel.Messages);
        Assert.True(userMessage.IsUser);
        Assert.True(viewModel.IsProviderRunning);
        Assert.Equal(2, provider.DetectCount);
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
        Assert.False(viewModel.ConfigurationGate.IsVisible);
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

        TaskCompletionSource<bool> finalProgressObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleProviderProgressChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs arguments)
        {
            if (arguments.PropertyName == nameof(MainViewModel.ProviderProgressValue)
                && viewModel.ProviderProgressValue == 100d)
            {
                finalProgressObserved.TrySetResult(true);
            }
        }

        viewModel.PropertyChanged += HandleProviderProgressChanged;

        try
        {
            await viewModel.InstallProviderCommand.ExecuteAsync(null).ConfigureAwait(true);
            await finalProgressObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        finally
        {
            viewModel.PropertyChanged -= HandleProviderProgressChanged;
        }

        Assert.True(viewModel.IsProviderProgressVisible);
        Assert.False(viewModel.IsProviderProgressIndeterminate);
        Assert.Equal(100d, viewModel.ProviderProgressValue);
        Assert.Contains("Installation complete", viewModel.ProviderProgressText, StringComparison.Ordinal);
        Assert.Equal("Test provider installed.", viewModel.ProviderProgressDetailText);
    }

    [Fact]
    public async Task ProviderUpdateCheckProjectsManagedValidatedAndLatestVersions()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            UpdateInfo = new InferenceProviderUpdateInfo(
                installedVersion: "b10434",
                validatedVersion: "b10435",
                latestVersion: "b10435",
                isManagedInstallation: true,
                isUpdateAvailable: true,
                isLatestVersionValidated: true,
                detail: "Validated update available."),
        };
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 0, 20, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.SupportsProviderUpdates);
        Assert.True(viewModel.CanCheckProviderUpdate);
        Assert.False(viewModel.CanUpdateProvider);
        Assert.Equal("Alicia validated b10435", viewModel.ProviderValidatedReleaseText);

        await viewModel.CheckProviderUpdateCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.CheckUpdateCount);
        Assert.Equal("Managed release b10434", viewModel.ProviderInstalledReleaseText);
        Assert.Equal("Alicia validated b10435", viewModel.ProviderValidatedReleaseText);
        Assert.Equal("Upstream latest b10435", viewModel.ProviderLatestReleaseText);
        Assert.Equal("Validated update available.", viewModel.ProviderUpdateStatusText);
        Assert.True(viewModel.IsProviderUpdateAvailable);
        Assert.True(viewModel.CanUpdateProvider);
        Assert.Equal(0, provider.UpdateCount);
    }

    [Fact]
    public async Task ProviderUpdateRunsOnlyAfterExplicitCommandAndReturnsToReady()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            UpdateInfo = new InferenceProviderUpdateInfo(
                installedVersion: "b10434",
                validatedVersion: "b10435",
                latestVersion: "b10435",
                isManagedInstallation: true,
                isUpdateAvailable: true,
                isLatestVersionValidated: true,
                detail: "Validated update available."),
        };
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 0, 21, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.CheckProviderUpdateCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(0, provider.UpdateCount);
        Assert.True(viewModel.CanUpdateProvider);

        await viewModel.UpdateProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.UpdateCount);
        Assert.Equal("Ready", viewModel.ProviderStatusText);
        Assert.Equal("Managed release b10435", viewModel.ProviderInstalledReleaseText);
        Assert.Equal("Managed runtime is up to date.", viewModel.ProviderUpdateStatusText);
        Assert.False(viewModel.IsProviderUpdateAvailable);
        Assert.False(viewModel.CanUpdateProvider);
        Assert.False(viewModel.HasProviderFailure);
        Assert.False(viewModel.HasError);
        Assert.Equal(100d, viewModel.ProviderProgressValue);
    }

    [Fact]
    public async Task ProviderUpdateFailureKeepsHealthyRuntimeReadyAndRetryable()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            UpdateInfo = new InferenceProviderUpdateInfo(
                installedVersion: "b10434",
                validatedVersion: "b10435",
                latestVersion: "b10435",
                isManagedInstallation: true,
                isUpdateAvailable: true,
                isLatestVersionValidated: true,
                detail: "Validated update available."),
            UpdateException = new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not download the validated llama.cpp update. Check the network connection and try again.",
                new HttpRequestException("private mirror token=secret")),
        };
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 0, 22, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.CheckProviderUpdateCommand.ExecuteAsync(null).ConfigureAwait(true);

        await viewModel.UpdateProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.UpdateCount);
        Assert.Equal("Ready", viewModel.ProviderStatusText);
        Assert.False(viewModel.HasProviderFailure);
        Assert.True(viewModel.HasError);
        string updateError = Assert.IsType<string>(viewModel.ErrorMessage);
        Assert.Equal(
            "Alicia could not download the validated llama.cpp update. Check the network connection and try again.",
            updateError);
        Assert.DoesNotContain("secret", updateError, StringComparison.Ordinal);
        Assert.Equal(updateError, viewModel.ProviderUpdateStatusText);
        Assert.True(viewModel.CanUpdateProvider);
    }

    [Fact]
    public async Task ProviderStorageInspectionIsRequiredBeforeDestructiveMaintenance()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.SupportsProviderMaintenance);
        Assert.True(viewModel.CanInspectProviderStorage);
        Assert.False(viewModel.HasProviderStorageInfo);
        Assert.False(viewModel.CanRequestProviderMaintenance);
        Assert.False(viewModel.CanCleanupRetainedProviderReleases);

        await viewModel.InspectProviderStorageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.InspectStorageCount);
        Assert.True(viewModel.HasProviderStorageInfo);
        Assert.True(viewModel.CanRequestProviderMaintenance);
        Assert.True(viewModel.CanCleanupRetainedProviderReleases);
        Assert.Contains("b10435", viewModel.ProviderRuntimeStorageText, StringComparison.Ordinal);
        Assert.Contains("model cache", viewModel.ProviderModelCacheStorageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 inactive", viewModel.ProviderRetainedReleasesText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderUninstallRequestDoesNotDeleteAnythingUntilExplicitConfirmation()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 1, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.InspectProviderStorageCommand.ExecuteAsync(null).ConfigureAwait(true);

        viewModel.RequestUninstallProviderRuntimeCommand.Execute(null);

        Assert.True(viewModel.IsProviderMaintenanceConfirmationVisible);
        Assert.Equal("Uninstall managed runtime?", viewModel.ProviderMaintenanceConfirmationTitle);
        Assert.Equal(0, provider.UninstallCount);

        viewModel.CancelProviderMaintenanceCommand.Execute(null);

        Assert.False(viewModel.IsProviderMaintenanceConfirmationVisible);
        Assert.Equal(0, provider.UninstallCount);
        Assert.True(provider.StorageInfo.HasManagedRuntime);
    }

    [Fact]
    public async Task ConfirmedRuntimeOnlyUninstallPreservesModelCacheScope()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 2, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.InspectProviderStorageCommand.ExecuteAsync(null).ConfigureAwait(true);
        string savedModelReference = viewModel.ProviderModelReference;

        viewModel.RequestUninstallProviderRuntimeCommand.Execute(null);
        await viewModel.ConfirmProviderMaintenanceCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.UninstallCount);
        Assert.Equal(InferenceProviderRemovalMode.RuntimeOnly, provider.LastRemovalMode);
        Assert.False(viewModel.IsProviderMaintenanceConfirmationVisible);
        Assert.Equal("Not installed", viewModel.ProviderStatusText);
        Assert.True(viewModel.HasProviderStorageInfo);
        Assert.Contains("0 B remaining runtime files", viewModel.ProviderRuntimeStorageText, StringComparison.Ordinal);
        Assert.DoesNotContain("0 B", viewModel.ProviderModelCacheStorageText, StringComparison.Ordinal);
        Assert.Contains("model cache", viewModel.ProviderMaintenanceStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(savedModelReference, viewModel.ProviderModelReference);
        Assert.True(viewModel.CanInstallProvider);
    }

    [Fact]
    public async Task ConfirmedRuntimeAndCacheUninstallUsesDistinctDestructiveScope()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 3, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.InspectProviderStorageCommand.ExecuteAsync(null).ConfigureAwait(true);

        viewModel.RequestUninstallProviderRuntimeAndCacheCommand.Execute(null);
        Assert.Equal(0, provider.UninstallCount);
        await viewModel.ConfirmProviderMaintenanceCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.UninstallCount);
        Assert.Equal(InferenceProviderRemovalMode.RuntimeAndModelCache, provider.LastRemovalMode);
        Assert.Equal("Local model cache • 0 B", viewModel.ProviderModelCacheStorageText);
        Assert.Contains("configuration and conversations preserved", viewModel.ProviderMaintenanceStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetainedReleaseCleanupRequiresConfirmationAndPreservesActiveRuntime()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 4, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.InspectProviderStorageCommand.ExecuteAsync(null).ConfigureAwait(true);

        viewModel.RequestCleanupRetainedProviderReleasesCommand.Execute(null);

        Assert.True(viewModel.IsProviderMaintenanceConfirmationVisible);
        Assert.Equal(0, provider.CleanupRetainedReleasesCount);
        await viewModel.ConfirmProviderMaintenanceCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.CleanupRetainedReleasesCount);
        Assert.False(viewModel.HasRetainedProviderReleases);
        Assert.False(viewModel.CanCleanupRetainedProviderReleases);
        Assert.Equal("Ready", viewModel.ProviderStatusText);
        Assert.True(provider.StorageInfo.HasManagedRuntime);
        Assert.True(provider.StorageInfo.ModelCacheBytes > 0);
        Assert.Equal(100d, viewModel.ProviderProgressValue);
    }

    [Fact]
    public async Task EscapeCancelsProviderMaintenanceConfirmationWithoutMutation()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 5, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.InspectProviderStorageCommand.ExecuteAsync(null).ConfigureAwait(true);
        viewModel.RequestUninstallProviderRuntimeAndCacheCommand.Execute(null);
        Assert.True(viewModel.IsProviderMaintenanceConfirmationVisible);

        viewModel.CancelTransientActionCommand.Execute(null);

        Assert.False(viewModel.IsProviderMaintenanceConfirmationVisible);
        Assert.Equal(0, provider.UninstallCount);
    }

    [Fact]
    public async Task RunningProviderAllowsInspectionButBlocksDestructiveMaintenance()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Running);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 6, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.IsProviderRunning);
        Assert.True(viewModel.CanInspectProviderStorage);
        await viewModel.InspectProviderStorageCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(viewModel.HasProviderStorageInfo);
        Assert.False(viewModel.CanRequestProviderMaintenance);

        viewModel.RequestUninstallProviderRuntimeCommand.Execute(null);
        Assert.False(viewModel.IsProviderMaintenanceConfirmationVisible);
        Assert.Equal(0, provider.UninstallCount);
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
    public async Task FailedModelStartReturnsToReadyWithSafeModelGuidance()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            StartException = new InferenceProviderException(
                InferenceProviderFailureKind.Model,
                "The selected model could not be loaded. Review the model settings and try again.",
                new InvalidOperationException(
                    "failed to load /home/user/private-model.gguf token=secret")),
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
        string modelError = Assert.IsType<string>(viewModel.ErrorMessage);
        Assert.Equal(
            "The selected model could not be loaded. Review the model settings and try again.",
            modelError);
        Assert.DoesNotContain("/home/user", modelError, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", modelError, StringComparison.Ordinal);
        Assert.Equal(InferenceProviderFailureKind.Model, viewModel.ProviderFailureKind);
        Assert.Equal("Model needs attention", viewModel.ProviderStatusText);
        Assert.True(viewModel.CanStartProvider);
        Assert.False(viewModel.CanInstallProvider);
        Assert.Equal(2, provider.DetectCount);
        Assert.Equal(
            ConversationConfigurationGateState.ModelConfigurationRequired,
            viewModel.ConfigurationGate.State);
        Assert.Equal("Review model", viewModel.ConfigurationGate.PrimaryActionLabel);
    }

    [Fact]
    public async Task NetworkStartFailureKeepsReadyRuntimeWithoutOfferingReinstall()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            StartException = new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not reach the local AI service. Check the connection and try again.",
                new HttpRequestException("connect 127.0.0.1:43123 failed")),
        };
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 26, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        await viewModel.StartProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.HasError);
        Assert.Equal(InferenceProviderFailureKind.Network, viewModel.ProviderFailureKind);
        Assert.Equal("Connection problem", viewModel.ProviderStatusText);
        Assert.False(viewModel.CanInstallProvider);
        Assert.True(viewModel.CanStartProvider);
        Assert.Equal(2, provider.DetectCount);
        Assert.Equal(
            ConversationConfigurationGateState.ProviderFaulted,
            viewModel.ConfigurationGate.State);
        Assert.Equal("Check provider again", viewModel.ConfigurationGate.PrimaryActionLabel);
    }

    [Fact]
    public async Task LegacyProviderFailureIsReplacedWithSafeGenericMessage()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            StartException = new InvalidOperationException(
                "private path /home/user/model.gguf api-key=secret"),
        };
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 26, 30, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        await viewModel.StartProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Faulted, viewModel.ProviderFailureKind);
        string providerError = Assert.IsType<string>(viewModel.ErrorMessage);
        Assert.Equal(
            "The local AI provider could not complete the operation. Review the provider and try again.",
            providerError);
        Assert.DoesNotContain("/home/user", providerError, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", providerError, StringComparison.Ordinal);
        Assert.False(viewModel.CanInstallProvider);
        Assert.Equal("Review provider", viewModel.ConfigurationGate.PrimaryActionLabel);
    }

    [Fact]
    public async Task SuccessfulProviderCheckClearsPreviousRecoverableFailure()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready)
        {
            StartException = new InferenceProviderException(
                InferenceProviderFailureKind.Model,
                "The selected model could not be loaded."),
        };
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 21, 26, 45, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        await viewModel.StartProviderCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(InferenceProviderFailureKind.Model, viewModel.ProviderFailureKind);

        provider.StartException = null;
        await viewModel.DetectProviderCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.False(viewModel.HasProviderFailure);
        Assert.Null(viewModel.ProviderFailureKind);
        Assert.Equal("Ready", viewModel.ProviderStatusText);
        Assert.Equal("Load model", viewModel.ConfigurationGate.PrimaryActionLabel);
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

        Assert.False(viewModel.IsComposerEnabled);
        Assert.False(viewModel.CanSendMessage);
        Assert.True(viewModel.ShowInlineConfigurationGate);
        Assert.False(viewModel.ShowMessageComposer);
        Assert.Equal(
            ConversationConfigurationGateState.ProviderMissing,
            viewModel.ConfigurationGate.State);
        Assert.Equal("Install provider", viewModel.ConfigurationGate.PrimaryActionLabel);
    }

    [Fact]
    public async Task ConfigurationGateInstallsThenLoadsReadyProvider()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 13, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        repository.Seed(new Conversation(
            ConversationId.New(),
            "Gate flow",
            createdAt,
            createdAt));
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Missing);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            inferenceProvider: provider);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(
            ConversationConfigurationGateState.ProviderMissing,
            viewModel.ConfigurationGate.State);
        Assert.True(viewModel.ConfigurationGate.IsPrimaryActionEnabled);

        await viewModel.ConfigurationGatePrimaryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.InstallCount);
        Assert.Equal(
            ConversationConfigurationGateState.ProviderReadyToStart,
            viewModel.ConfigurationGate.State);
        Assert.Equal("Load model", viewModel.ConfigurationGate.PrimaryActionLabel);

        await viewModel.ConfigurationGatePrimaryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, provider.StartCount);
        Assert.True(viewModel.IsProviderRunning);
        Assert.False(viewModel.ConfigurationGate.IsVisible);
        Assert.True(viewModel.ShowMessageComposer);
        Assert.True(viewModel.IsComposerEnabled);
    }

    [Fact]
    public async Task ConfigurationGateRequiresModelConfigurationForReadyProviderWithoutSavedModel()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 13, 35, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        repository.Seed(new Conversation(
            ConversationId.New(),
            "Needs model",
            createdAt,
            createdAt));
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        StubInferenceProviderConfigurationStore configurationStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            inferenceProvider: provider,
            providerConfigurationStore: configurationStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(
            ConversationConfigurationGateState.ModelConfigurationRequired,
            viewModel.ConfigurationGate.State);
        Assert.Equal("Configure model", viewModel.ConfigurationGate.PrimaryActionLabel);
        Assert.True(viewModel.ShowInlineConfigurationGate);
        Assert.False(viewModel.ShowMessageComposer);
    }

    [Fact]
    public async Task ConfigurationGateUsesCentralOnboardingAndHidesEmptyHistoryWhenAiIsUnavailable()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Missing);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 13, 13, 40, 0, TimeSpan.Zero)),
            inferenceProvider: provider);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.ShowConfigurationOnboarding);
        Assert.False(viewModel.ShowInlineConfigurationGate);
        Assert.False(viewModel.ShowReadyEmptyConversationState);
        Assert.False(viewModel.ShowConversationHistoryPanel);
        Assert.False(viewModel.ShowConversationHistoryReopenButton);
    }

    [Fact]
    public async Task ConfigurationGateRoutesEmptyProviderRegistryToProviderSetup()
    {
        InMemoryConversationRepository repository = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 13, 13, 45, 0, TimeSpan.Zero)),
            providerRegistry: new EmptyInferenceProviderRegistry(),
            providerConfigurationStore: new StubInferenceProviderConfigurationStore());

        ShellViewModel shell = new(viewModel);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(
            ConversationConfigurationGateState.NoProvider,
            viewModel.ConfigurationGate.State);
        Assert.Equal("Configure provider", viewModel.ConfigurationGate.PrimaryActionLabel);
        Assert.True(viewModel.ShowConfigurationOnboarding);

        await viewModel.ConfigurationGatePrimaryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(WorkspaceSection.Providers, shell.SelectedSection);
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
        Assert.Equal("Review model settings", viewModel.ConfigurationGate.PrimaryActionLabel);

        await viewModel.SaveProviderConfigurationCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(1, configurationStore.SaveCount);
        Assert.False(viewModel.HasProviderConfigurationChanges);
        Assert.True(viewModel.CanStartProvider);
        Assert.Equal(
            ConversationConfigurationGateState.ProviderReadyToStart,
            viewModel.ConfigurationGate.State);
        Assert.Equal("Load model", viewModel.ConfigurationGate.PrimaryActionLabel);
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
        viewModel.ProviderReasoningEnabled = true;
        viewModel.ProviderReasoningBudgetText = "384";

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
        Assert.Equal(true, saved.Generation.ReasoningEnabled);
        Assert.Equal(384, saved.Generation.ReasoningBudgetTokens);
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
    public async Task BlankSamplingSettingsPreserveDefaultsWhileReasoningChoiceIsExplicit()
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
        Assert.False(saved.Generation.ReasoningEnabled);
        Assert.Null(saved.Generation.ReasoningBudgetTokens);
        Assert.False(saved.Generation.UsesOnlyProviderDefaults);
        Assert.False(saved.UsesProviderDefaults);
        Assert.Contains("Explicit", viewModel.ProviderConfigurationStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseStopAndRetryAreBrieflyLockedAroundCancellation()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 0, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        PausingStreamingConversationResponder responder = new(
            "Partial",
            " response");
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            responder,
            responseStopLockDuration: TimeSpan.FromMilliseconds(50),
            retryResponseLockDuration: TimeSpan.FromMilliseconds(50));
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Guard controls";

        Task sendTask = viewModel.SendMessageCommand.ExecuteAsync(null);
        await responder.FirstChunkObserved.ConfigureAwait(true);

        Assert.True(viewModel.IsGeneratingResponse);
        Assert.False(viewModel.CanStopResponse);

        await Task.Delay(150, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(viewModel.CanStopResponse);

        viewModel.StopResponseCommand.Execute(null);
        await sendTask.ConfigureAwait(true);

        Assert.False(viewModel.CanRetryResponse);
        await Task.Delay(150, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(viewModel.CanRetryResponse);
    }

    [Fact]
    public async Task ReasoningStreamProjectsStepsButPersistsOnlyFinalAssistantContent()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 0, 35, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        repository.Seed(conversation);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            new ReasoningConversationResponder());
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Reason about this";

        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Collection(
            viewModel.Messages,
            message => Assert.True(message.IsUser),
            message =>
            {
                Assert.True(message.IsAssistant);
                Assert.Equal("Final answer", message.Content);
                Assert.True(message.HasReasoning);
                Assert.False(message.IsReasoningExpanded);
                Assert.Collection(
                    message.ReasoningSteps,
                    step => Assert.Equal("Inspect premise", step.Content),
                    step => Assert.Equal("Verify result", step.Content));
            });
        Conversation persisted = Assert.IsType<Conversation>(
            await repository.FindAsync(conversation.Id, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("Final answer", persisted.Messages[^1].Content);
        Assert.DoesNotContain("Inspect premise", persisted.Messages[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnabledReasoningRequiresPositiveBudgetBeforeSettingsCanBeSaved()
    {
        InMemoryConversationRepository repository = new();
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 13, 0, 40, 0, TimeSpan.Zero)),
            inferenceProvider: provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.ProviderReasoningEnabled = true;
        viewModel.ProviderReasoningBudgetText = string.Empty;

        Assert.True(viewModel.HasProviderConfigurationValidationError);
        Assert.Contains("Reasoning budget", viewModel.ProviderConfigurationValidationText, StringComparison.Ordinal);
        Assert.False(viewModel.CanSaveProviderConfiguration);

        viewModel.ProviderReasoningBudgetText = "256";

        Assert.False(viewModel.HasProviderConfigurationValidationError);
    }

    [Fact]
    public async Task ConversationIdentityRestoresChangesAndPersistsWithoutChangingSelection()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 0, 45, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation older = new(ConversationId.New(), "Older", createdAt, createdAt);
        Conversation newer = new(
            ConversationId.New(),
            "Newer",
            createdAt.AddMinutes(1),
            createdAt.AddMinutes(1));
        repository.Seed(older);
        repository.Seed(newer);

        StubConversationUiStateStore uiStateStore = new(
            ConversationUiStateSnapshot.Default.WithConversationIdentity(
                older.Id,
                new ConversationVisualIdentity(
                    ConversationIdentityIcon.Code,
                    ConversationIdentityColor.Violet)));
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(2)),
            conversationUiStateStore: uiStateStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(newer.Id, viewModel.SelectedConversation?.Id);
        ConversationListItemViewModel olderItem = Assert.Single(
            viewModel.Conversations,
            item => item.Id == older.Id);
        Assert.Equal(ConversationIdentityIcon.Code, olderItem.Identity.Icon);
        Assert.Equal(ConversationIdentityColor.Violet, olderItem.Identity.Color);

        ConversationIdentityChoiceViewModel research = Assert.Single(
            olderItem.IconChoices,
            choice => string.Equals(choice.Label, "Research", StringComparison.Ordinal));
        await research.SelectCommand.ExecuteAsync(null).ConfigureAwait(true);
        ConversationIdentityChoiceViewModel orange = Assert.Single(
            olderItem.ColorChoices,
            choice => string.Equals(choice.Label, "Orange", StringComparison.Ordinal));
        await orange.SelectCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(newer.Id, viewModel.SelectedConversation?.Id);
        Assert.Equal(ConversationIdentityIcon.Research, olderItem.Identity.Icon);
        Assert.Equal(ConversationIdentityColor.Orange, olderItem.Identity.Color);
        Assert.Equal(2, uiStateStore.SaveCount);
        Assert.Equal(
            olderItem.Identity,
            uiStateStore.Snapshot.GetConversationIdentity(older.Id));

        await viewModel.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);

        ConversationListItemViewModel restored = Assert.Single(
            viewModel.Conversations,
            item => item.Id == older.Id);
        Assert.Equal(ConversationIdentityIcon.Research, restored.Identity.Icon);
        Assert.Equal(ConversationIdentityColor.Orange, restored.Identity.Color);
    }

    [Fact]
    public async Task ConversationUiStateRestoresPerConversationAfterInitializationAndSelection()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation older = new(ConversationId.New(), "Older", createdAt, createdAt);
        older.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Older message",
            createdAt.AddMinutes(1)));
        repository.Seed(older);

        Conversation newer = new(
            ConversationId.New(),
            "Newer",
            createdAt.AddHours(1),
            createdAt.AddHours(1));
        newer.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Newer message",
            createdAt.AddHours(1).AddMinutes(1)));
        repository.Seed(newer);

        ConversationUiStateSnapshot snapshot = ConversationUiStateSnapshot.Default
            .WithConversationScrollState(
                newer.Id,
                new ConversationScrollState(ConversationScrollMode.Detached, 144))
            .WithConversationScrollState(
                older.Id,
                new ConversationScrollState(ConversationScrollMode.Detached, 72));
        StubConversationUiStateStore uiStateStore = new(snapshot);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddHours(2)),
            conversationUiStateStore: uiStateStore);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(newer.Id, viewModel.SelectedConversation?.Id);
        Assert.Equal(ConversationScrollMode.Detached, viewModel.CurrentConversationScrollMode);
        Assert.Equal(144d, viewModel.CurrentConversationScrollOffset);
        Assert.True(viewModel.ShowScrollToLatestButton);

        ConversationListItemViewModel olderItem = Assert.Single(
            viewModel.Conversations,
            item => item.Id == older.Id);
        await olderItem.SelectCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(older.Id, viewModel.SelectedConversation?.Id);
        Assert.Equal(ConversationScrollMode.Detached, viewModel.CurrentConversationScrollMode);
        Assert.Equal(72d, viewModel.CurrentConversationScrollOffset);
    }

    [Fact]
    public async Task ScrollDetachesPastThresholdAndOnlyExplicitActionRefollows()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 15, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Scroll", createdAt, createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Long answer",
            createdAt.AddMinutes(1)));
        repository.Seed(conversation);
        StubConversationUiStateStore uiStateStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddHours(1)),
            conversationUiStateStore: uiStateStore);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.ReportConversationScrollPosition(689, 720, userMovedUp: true);
        Assert.Equal(ConversationScrollMode.Following, viewModel.CurrentConversationScrollMode);

        viewModel.ReportConversationScrollPosition(688, 720, userMovedUp: true);
        Assert.Equal(ConversationScrollMode.Detached, viewModel.CurrentConversationScrollMode);
        Assert.Equal(688d, viewModel.CurrentConversationScrollOffset);
        Assert.True(viewModel.ShowScrollToLatestButton);

        viewModel.ReportConversationScrollPosition(720, 720, userMovedUp: false);
        Assert.Equal(ConversationScrollMode.Detached, viewModel.CurrentConversationScrollMode);
        Assert.Equal(720d, viewModel.CurrentConversationScrollOffset);

        await viewModel.ScrollToLatestCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(ConversationScrollMode.Following, viewModel.CurrentConversationScrollMode);
        Assert.False(viewModel.ShowScrollToLatestButton);
        Assert.Equal(1, uiStateStore.SaveCount);
    }


    [Fact]
    public async Task PauseAutoScrollDetachesWithoutRequiringScrollGesture()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 22, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Manual scroll", createdAt, createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Long answer",
            createdAt.AddMinutes(1)));
        repository.Seed(conversation);
        StubConversationUiStateStore uiStateStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddHours(1)),
            conversationUiStateStore: uiStateStore);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.ReportConversationScrollPosition(720, 720, userMovedUp: false);

        Assert.True(viewModel.ShowPauseAutoScrollButton);
        Assert.False(viewModel.ShowScrollToLatestButton);

        await viewModel.PauseAutoScrollCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(ConversationScrollMode.Detached, viewModel.CurrentConversationScrollMode);
        Assert.False(viewModel.ShowPauseAutoScrollButton);
        Assert.True(viewModel.ShowScrollToLatestButton);
        Assert.Equal(1, uiStateStore.SaveCount);

        await viewModel.ScrollToLatestCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(ConversationScrollMode.Following, viewModel.CurrentConversationScrollMode);
        Assert.True(viewModel.ShowPauseAutoScrollButton);
        Assert.False(viewModel.ShowScrollToLatestButton);
        Assert.Equal(2, uiStateStore.SaveCount);
    }

    [Fact]
    public async Task CompletedGenerationRefreshesProviderObservabilityProjection()
    {
        DateTimeOffset createdAt = new(2026, 8, 16, 19, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        repository.Seed(new Conversation(
            ConversationId.New(),
            "Observability",
            createdAt,
            createdAt));
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Running);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            new DeterministicConversationResponder("Observed response"),
            provider);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.SupportsProviderObservability);
        Assert.False(viewModel.HasProviderGenerationObservation);

        provider.LatestGenerationObservation = new InferenceProviderGenerationObservation(
            providerId: "llama.cpp.cuda",
            providerName: "llama.cpp CUDA",
            modelReference: "Qwen/Qwen3-4B-GGUF",
            runtimeVersion: "b10435",
            startedAtUtc: createdAt.AddSeconds(10),
            completedAtUtc: createdAt.AddSeconds(12),
            outcome: InferenceProviderGenerationOutcome.Completed,
            failureKind: null,
            duration: TimeSpan.FromSeconds(2),
            timeToFirstOutput: TimeSpan.FromMilliseconds(250),
            inputTokens: 120,
            outputTokens: 30,
            totalTokens: 150,
            cachedInputTokens: 80,
            promptEvaluationDuration: TimeSpan.FromMilliseconds(40),
            generationDuration: TimeSpan.FromMilliseconds(800),
            promptTokensPerSecond: 3000,
            generationTokensPerSecond: 37.5);

        viewModel.MessageDraft = "Observe this";
        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.HasProviderGenerationObservation);
        Assert.Equal("Last generation completed", viewModel.ProviderGenerationOutcomeText);
        Assert.Contains("Qwen/Qwen3-4B-GGUF", viewModel.ProviderGenerationIdentityText, StringComparison.Ordinal);
        Assert.Contains("first output", viewModel.ProviderGenerationLatencyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("input 120", viewModel.ProviderGenerationTokenUsageText, StringComparison.Ordinal);
        string expectedGenerationRate = 37.5.ToString(
            "0.0",
            System.Globalization.CultureInfo.CurrentCulture);
        Assert.Contains(
            $"generation {expectedGenerationRate} tok/s",
            viewModel.ProviderGenerationTimingText,
            StringComparison.Ordinal);
        Assert.Contains("does not write prompt or message content", viewModel.ProviderObservabilityPrivacyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendingAUserMessageReturnsDetachedConversationToFollowing()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Send", createdAt, createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Previous answer",
            createdAt.AddMinutes(1)));
        repository.Seed(conversation);
        StubConversationUiStateStore uiStateStore = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddHours(1)),
            conversationUiStateStore: uiStateStore);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.ReportConversationScrollPosition(300, 500, userMovedUp: true);
        Assert.Equal(ConversationScrollMode.Detached, viewModel.CurrentConversationScrollMode);

        viewModel.MessageDraft = "Continue";
        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(ConversationScrollMode.Following, viewModel.CurrentConversationScrollMode);
        Assert.False(viewModel.ShowScrollToLatestButton);
        Assert.True(uiStateStore.SaveCount >= 1);
    }

    [Fact]
    public async Task NarrowHistoryOverlayPreservesWidePreferenceAcrossOpenAndClose()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 45, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        StubConversationUiStateStore uiStateStore = new(ConversationUiStateSnapshot.Default);
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt),
            conversationUiStateStore: uiStateStore);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.IsConversationHistoryExpanded);
        Assert.False(viewModel.IsNarrowConversationLayout);
        Assert.True(viewModel.ShowWideConversationHistoryPanel);
        Assert.False(viewModel.ShowNarrowConversationHistoryPanel);
        viewModel.SetConversationHistoryNarrowLayout(isNarrow: true);

        Assert.True(viewModel.IsNarrowConversationLayout);
        Assert.True(viewModel.IsConversationHistoryCollapsed);
        Assert.False(viewModel.ShowWideConversationHistoryPanel);
        Assert.False(viewModel.ShowNarrowConversationHistoryPanel);
        Assert.False(viewModel.ShowNarrowHistoryBackdrop);
        Assert.True(uiStateStore.Snapshot.IsConversationHistoryExpanded);

        await viewModel.ToggleConversationHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.IsConversationHistoryExpanded);
        Assert.False(viewModel.ShowWideConversationHistoryPanel);
        Assert.True(viewModel.ShowNarrowConversationHistoryPanel);
        Assert.True(viewModel.ShowNarrowHistoryBackdrop);
        Assert.Equal(0, uiStateStore.SaveCount);

        await viewModel.ToggleConversationHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.IsConversationHistoryCollapsed);
        Assert.False(viewModel.ShowNarrowHistoryBackdrop);
        Assert.Equal(0, uiStateStore.SaveCount);

        viewModel.SetConversationHistoryNarrowLayout(isNarrow: false);

        Assert.False(viewModel.IsNarrowConversationLayout);
        Assert.True(viewModel.IsConversationHistoryExpanded);
        Assert.True(viewModel.ShowWideConversationHistoryPanel);
        Assert.False(viewModel.ShowNarrowConversationHistoryPanel);

        await viewModel.ToggleConversationHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.IsConversationHistoryCollapsed);
        Assert.False(uiStateStore.Snapshot.IsConversationHistoryExpanded);
        Assert.Equal(1, uiStateStore.SaveCount);

        viewModel.SetConversationHistoryNarrowLayout(isNarrow: true);
        await viewModel.ToggleConversationHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(viewModel.IsConversationHistoryExpanded);
        Assert.True(viewModel.ShowNarrowHistoryBackdrop);
        Assert.False(uiStateStore.Snapshot.IsConversationHistoryExpanded);
        Assert.Equal(1, uiStateStore.SaveCount);

        viewModel.SetConversationHistoryNarrowLayout(isNarrow: false);

        Assert.True(viewModel.IsConversationHistoryCollapsed);
        Assert.False(uiStateStore.Snapshot.IsConversationHistoryExpanded);
    }


    [Fact]
    public async Task NarrowHistoryOverlayClosesAfterConversationSelection()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 50, 0, TimeSpan.Zero);
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

        viewModel.SetConversationHistoryNarrowLayout(isNarrow: true);
        await viewModel.ToggleConversationHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(viewModel.ShowNarrowHistoryBackdrop);

        ConversationListItemViewModel olderItem = Assert.Single(
            viewModel.Conversations,
            item => item.Id == older.Id);
        await olderItem.SelectCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(older.Id, viewModel.SelectedConversation?.Id);
        Assert.True(viewModel.IsConversationHistoryCollapsed);
        Assert.False(viewModel.ShowNarrowHistoryBackdrop);
    }

    [Fact]
    public async Task NarrowHistoryOverlayClosesAfterCreatingConversation()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 55, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.SetConversationHistoryNarrowLayout(isNarrow: true);
        await viewModel.ToggleConversationHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(viewModel.ShowNarrowHistoryBackdrop);

        await viewModel.CreateConversationCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.NotNull(viewModel.SelectedConversation);
        Assert.True(viewModel.IsConversationHistoryCollapsed);
        Assert.False(viewModel.ShowNarrowHistoryBackdrop);
    }

    [Fact]
    public async Task CancelTransientActionClosesNarrowHistoryBeforeStoppingStreamingResponse()
    {
        DateTimeOffset createdAt = new(2026, 8, 13, 1, 58, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Streaming", createdAt, createdAt);
        repository.Seed(conversation);
        PausingStreamingConversationResponder responder = new("Partial ", "response");
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(createdAt.AddMinutes(1)),
            responder);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.SetConversationHistoryNarrowLayout(isNarrow: true);
        await viewModel.ToggleConversationHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(viewModel.ShowNarrowConversationHistoryPanel);

        viewModel.MessageDraft = "Continue";
        Task sendTask = viewModel.SendMessageCommand.ExecuteAsync(null);
        await responder.FirstChunkObserved.ConfigureAwait(true);
        Assert.True(viewModel.IsGeneratingResponse);

        viewModel.CancelTransientActionCommand.Execute(null);

        Assert.False(viewModel.ShowNarrowConversationHistoryPanel);
        Assert.True(viewModel.IsGeneratingResponse);

        responder.Release();
        await sendTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task ReducedMotionPreferenceIsProjectedToPresentation()
    {
        InMemoryConversationRepository repository = new();
        MainViewModel viewModel = CreateViewModel(
            repository,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 13, 2, 0, 0, TimeSpan.Zero)),
            isReducedMotionEnabled: true);

        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.True(viewModel.IsReducedMotionEnabled);
        Assert.False(viewModel.IsStandardMotionEnabled);
        Assert.False(viewModel.IsProviderProgressIndeterminateAnimationEnabled);
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
        IInferenceProviderConfigurationStore? providerConfigurationStore = null,
        TimeSpan? responseStopLockDuration = null,
        TimeSpan? retryResponseLockDuration = null,
        IConversationUiStateStore? conversationUiStateStore = null,
        bool isReducedMotionEnabled = false,
        IGenerationProfileCatalogStore? generationProfileCatalogStore = null,
        IConversationBranchGenerationSelectionStore? conversationGenerationSelectionStore = null)
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
                    "owner/model-GGUF:Q4_K_M",
                    generation: new InferenceGenerationOptions(reasoningEnabled: false)));

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
            resolvedConfigurationStore,
            responseStopLockDuration ?? TimeSpan.Zero,
            retryResponseLockDuration ?? TimeSpan.Zero,
            conversationUiStateStore,
            isReducedMotionEnabled,
            generationProfileCatalogStore,
            conversationGenerationSelectionStore);
    }
}
