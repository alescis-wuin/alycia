using Alicia.Application.Conversations;

namespace Alicia.Infrastructure.Conversations;

public sealed class LocalConversationRuntime
{
    private LocalConversationRuntime(
        IConversationRepository repository,
        CreateConversationUseCase createConversation,
        AppendMessageUseCase appendMessage)
    {
        Repository = repository;
        CreateConversation = createConversation;
        AppendMessage = appendMessage;
    }

    public IConversationRepository Repository { get; }

    public CreateConversationUseCase CreateConversation { get; }

    public AppendMessageUseCase AppendMessage { get; }

    public static LocalConversationRuntime Create(
        string storageDirectory,
        TimeProvider? timeProvider = null)
    {
        TimeProvider resolvedTimeProvider = timeProvider ?? TimeProvider.System;
        JsonConversationRepository repository = new(storageDirectory);

        return new LocalConversationRuntime(
            repository,
            new CreateConversationUseCase(repository, resolvedTimeProvider),
            new AppendMessageUseCase(repository, resolvedTimeProvider));
    }
}
