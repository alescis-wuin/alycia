using Alicia.Application.Conversations;

namespace Alicia.Infrastructure.Conversations;

public sealed class LocalConversationRuntime
{
    private LocalConversationRuntime(
        IConversationRepository repository,
        CreateConversationUseCase createConversation,
        AppendMessageUseCase appendMessage,
        LoadConversationUseCase loadConversation,
        ListConversationsUseCase listConversations,
        RenameConversationUseCase renameConversation,
        DeleteConversationUseCase deleteConversation)
    {
        Repository = repository;
        CreateConversation = createConversation;
        AppendMessage = appendMessage;
        LoadConversation = loadConversation;
        ListConversations = listConversations;
        RenameConversation = renameConversation;
        DeleteConversation = deleteConversation;
    }

    public IConversationRepository Repository { get; }

    public CreateConversationUseCase CreateConversation { get; }

    public AppendMessageUseCase AppendMessage { get; }

    public LoadConversationUseCase LoadConversation { get; }

    public ListConversationsUseCase ListConversations { get; }

    public RenameConversationUseCase RenameConversation { get; }

    public DeleteConversationUseCase DeleteConversation { get; }

    public static LocalConversationRuntime Create(
        string storageDirectory,
        TimeProvider? timeProvider = null)
    {
        TimeProvider resolvedTimeProvider = timeProvider ?? TimeProvider.System;
        JsonConversationRepository repository = new(storageDirectory);

        return new LocalConversationRuntime(
            repository,
            new CreateConversationUseCase(repository, resolvedTimeProvider),
            new AppendMessageUseCase(repository, resolvedTimeProvider),
            new LoadConversationUseCase(repository),
            new ListConversationsUseCase(repository),
            new RenameConversationUseCase(repository, resolvedTimeProvider),
            new DeleteConversationUseCase(repository));
    }
}
