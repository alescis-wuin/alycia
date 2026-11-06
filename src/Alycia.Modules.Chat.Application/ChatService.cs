using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed class ChatService(IChatRepository repository, IChatModel model, ChatOptions options)
{
    public async Task<Conversation> CreateAsync(string? title, string? modelName, string? systemPrompt,
        CancellationToken cancellationToken)
    {
        var conversation = Conversation.Create(title, modelName, systemPrompt);
        // Validate the system instruction before accepting a conversation.
        SlidingContext.Build(conversation.SystemPrompt, [], "Bonjour", options);
        var models = await model.ListModelsAsync(cancellationToken);
        if (!models.Contains(conversation.Model, StringComparer.Ordinal))
        {
            throw new ArgumentException("Choisissez un modèle local installé dans Ollama, puis actualisez la liste.");
        }

        await repository.AddAsync(conversation, cancellationToken);
        return conversation;
    }

    public async Task<SendMessageResult> SendAsync(Guid id, Guid requestId, string? content,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Un identifiant de requête est requis.");
        }

        var text = Conversation.RequiredText(content, 32000, "Le message");
        var conversation = await repository.FindAsync(id, cancellationToken)
            ?? throw new ConversationNotFoundException();
        var previous = await repository.FindTurnAsync(id, requestId, cancellationToken);
        if (previous.Count > 0)
        {
            if (previous.Count != 2 || previous[0].Content != text)
            {
                throw new ChatConflictException();
            }

            return new(previous, null, true);
        }

        var history = await repository.MessagesAsync(id, options.MaxHistoryTurns * 2, null, cancellationToken);
        var window = SlidingContext.Build(conversation.SystemPrompt, history, text, options);
        var reply = await model.GenerateAsync(conversation.Model, window.Messages, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(reply) || reply.Contains('\0'))
        {
            throw new ChatModelException("Ollama a renvoyé une réponse vide ou invalide. Essayez un autre modèle.");
        }

        var user = new ChatMessage(id, conversation.MessageCount + 1, requestId, "user", text);
        var assistant = new ChatMessage(id, conversation.MessageCount + 2, requestId, "assistant", reply);
        conversation.CompleteTurn();
        // One atomic SaveChanges for both messages and the revision. A failed
        // model call/cancellation never leaves an orphan user message in storage.
        await repository.AppendTurnAsync(conversation, user, assistant, cancellationToken);
        return new([user, assistant], window.HistoryMessagesIncluded, false);
    }
}
