using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.ViewModels;

public sealed class ConversationHistoryViewModelTests
{
    [Fact]
    public async Task SearchRanksExactPrefixPartialAndContentMatchesInThatOrder()
    {
        DateTimeOffset createdAt = new(2026, 8, 12, 4, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();

        Conversation exact = new(ConversationId.New(), "alpha", createdAt, createdAt);
        repository.Seed(exact);

        Conversation prefix = new(
            ConversationId.New(),
            "alpha project",
            createdAt.AddMinutes(1),
            createdAt.AddMinutes(1));
        repository.Seed(prefix);

        Conversation partial = new(
            ConversationId.New(),
            "notes alpha archive",
            createdAt.AddMinutes(2),
            createdAt.AddMinutes(2));
        repository.Seed(partial);

        Conversation content = new(
            ConversationId.New(),
            "other topic",
            createdAt.AddMinutes(3),
            createdAt.AddMinutes(3));
        content.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "This message contains the alpha keyword.",
            createdAt.AddMinutes(4)));
        repository.Seed(content);

        Conversation noMatch = new(
            ConversationId.New(),
            "unrelated",
            createdAt.AddMinutes(5),
            createdAt.AddMinutes(5));
        noMatch.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Nothing relevant is here.",
            createdAt.AddMinutes(6)));
        repository.Seed(noMatch);

        MainViewModel viewModel = CreateViewModel(repository, createdAt.AddHours(1));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.HistorySearchText = "alpha";
        await WaitForSearchAsync(viewModel).ConfigureAwait(true);

        Assert.Collection(
            viewModel.Conversations,
            item => Assert.Equal(exact.Id, item.Id),
            item => Assert.Equal(prefix.Id, item.Id),
            item => Assert.Equal(partial.Id, item.Id),
            item => Assert.Equal(content.Id, item.Id));
    }

    [Fact]
    public async Task ContentSearchBuildsPreviewAroundMatchingMessage()
    {
        DateTimeOffset createdAt = new(2026, 8, 12, 4, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            "Research",
            createdAt,
            createdAt);

        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Before the match",
            createdAt.AddMinutes(1)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "The needle is in this response.",
            createdAt.AddMinutes(2)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "After the match",
            createdAt.AddMinutes(3)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Latest unrelated message",
            createdAt.AddMinutes(4)));
        repository.Seed(conversation);

        MainViewModel viewModel = CreateViewModel(repository, createdAt.AddHours(1));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.HistorySearchText = "needle";
        await WaitForSearchAsync(viewModel).ConfigureAwait(true);

        ConversationListItemViewModel item = Assert.Single(viewModel.Conversations);
        Assert.Equal(3, item.PreviewMessages.Count);
        Assert.Contains(
            item.PreviewMessages,
            preview => preview.Content.Contains("needle", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Before the match", item.PreviewMessages[0].Content);
    }

    [Fact]
    public async Task HoverPreviewLoadsOnlyThreeMostRecentMessages()
    {
        DateTimeOffset createdAt = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            "Preview",
            createdAt,
            createdAt);

        for (int index = 1; index <= 5; index++)
        {
            conversation.AddMessage(new ChatMessage(
                MessageId.New(),
                index % 2 == 0 ? MessageRole.Assistant : MessageRole.User,
                $"Message {index}",
                createdAt.AddMinutes(index)));
        }

        repository.Seed(conversation);

        MainViewModel viewModel = CreateViewModel(repository, createdAt.AddHours(1));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        ConversationListItemViewModel item = Assert.Single(viewModel.Conversations);
        await item.EnsurePreviewLoadedAsync().ConfigureAwait(true);

        Assert.Collection(
            item.PreviewMessages,
            preview => Assert.Equal("Message 3", preview.Content),
            preview => Assert.Equal("Message 4", preview.Content),
            preview => Assert.Equal("Message 5", preview.Content));
    }

    [Fact]
    public async Task SearchNoResultStateDoesNotMasqueradeAsEmptyHistoryAndClearingRestoresHistory()
    {
        DateTimeOffset createdAt = new(2026, 8, 12, 5, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        repository.Seed(new Conversation(
            ConversationId.New(),
            "One",
            createdAt,
            createdAt));
        repository.Seed(new Conversation(
            ConversationId.New(),
            "Two",
            createdAt.AddMinutes(1),
            createdAt.AddMinutes(1)));

        MainViewModel viewModel = CreateViewModel(repository, createdAt.AddHours(1));
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.HistorySearchText = "not-present";
        await WaitForSearchAsync(viewModel).ConfigureAwait(true);

        Assert.Empty(viewModel.Conversations);
        Assert.True(viewModel.ShowNoHistorySearchResults);
        Assert.False(viewModel.IsHistoryEmpty);
        Assert.True(viewModel.HasConversationHistory);

        viewModel.HistorySearchText = string.Empty;
        await WaitForSearchAsync(viewModel).ConfigureAwait(true);

        Assert.Equal(2, viewModel.Conversations.Count);
        Assert.False(viewModel.ShowNoHistorySearchResults);
    }

    [Fact]
    public async Task HistoryCollapseTogglePreservesConversationSelectionAndDraft()
    {
        DateTimeOffset createdAt = new(2026, 8, 12, 6, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            "State",
            createdAt,
            createdAt);
        repository.Seed(conversation);

        MainViewModel viewModel = CreateViewModel(repository, createdAt.AddHours(1));
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Keep this draft";

        ConversationListItemViewModel selected = Assert.IsType<ConversationListItemViewModel>(
            viewModel.SelectedConversation);

        viewModel.ToggleConversationHistoryCommand.Execute(null);

        Assert.True(viewModel.IsConversationHistoryCollapsed);
        Assert.Same(selected, viewModel.SelectedConversation);
        Assert.Equal("Keep this draft", viewModel.MessageDraft);

        viewModel.ToggleConversationHistoryCommand.Execute(null);

        Assert.True(viewModel.IsConversationHistoryExpanded);
        Assert.Same(selected, viewModel.SelectedConversation);
        Assert.Equal("Keep this draft", viewModel.MessageDraft);
    }

    private static async Task WaitForSearchAsync(MainViewModel viewModel)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            if (!viewModel.IsHistorySearchBusy)
            {
                return;
            }

            await Task.Yield();
        }

        Assert.False(viewModel.IsHistorySearchBusy, "Conversation-history search did not settle.");
    }

    private static MainViewModel CreateViewModel(
        IConversationRepository repository,
        DateTimeOffset now)
    {
        MutableTimeProvider timeProvider = new(now);
        StubInferenceProviderRuntime provider = new();
        StubInferenceProviderRegistry registry = new(provider);
        StubInferenceProviderConfigurationStore configurationStore = new(
            new InferenceProviderConfiguration(
                "llama.cpp.cuda",
                "owner/model-GGUF:Q4_K_M"));

        return new MainViewModel(
            new CreateConversationUseCase(repository, timeProvider),
            new AppendMessageUseCase(repository, timeProvider),
            new StreamConversationTurnUseCase(
                repository,
                new DeterministicConversationResponder("Development response"),
                timeProvider),
            new LoadConversationUseCase(repository),
            new ListConversationsUseCase(repository),
            new RenameConversationUseCase(repository, timeProvider),
            new DeleteConversationUseCase(repository),
            registry,
            configurationStore);
    }
}
