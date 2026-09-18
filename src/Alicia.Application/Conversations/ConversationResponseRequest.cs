using System.Collections.ObjectModel;
using Alicia.Application.Generations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class ConversationResponseRequest
{
    private readonly ReadOnlyCollection<ChatMessage> _messages;

    public ConversationResponseRequest(
        ConversationId conversationId,
        string title,
        IEnumerable<ChatMessage> messages,
        GenerationSnapshot? generationSnapshot = null,
        string? systemInstructions = null)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Conversation title cannot be empty or whitespace.",
                nameof(title));
        }

        ArgumentNullException.ThrowIfNull(messages);

        ChatMessage[] messageSnapshot = messages.ToArray();
        ValidateGenerationSnapshot(
            conversationId,
            messageSnapshot,
            generationSnapshot);

        ConversationId = conversationId;
        Title = title;
        _messages = Array.AsReadOnly(messageSnapshot);
        GenerationSnapshot = generationSnapshot;
        SystemInstructions = string.IsNullOrWhiteSpace(systemInstructions)
            ? null
            : systemInstructions;
    }

    public ConversationId ConversationId { get; }

    public string Title { get; }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public GenerationSnapshot? GenerationSnapshot { get; }

    public string? SystemInstructions { get; }

    public static ConversationResponseRequest FromConversation(
        Conversation conversation,
        ResolvedConversationGeneration? resolvedGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        ConversationContextRevision? activeContext = conversation.ActiveContextRevision;
        GenerationSnapshot? snapshot = resolvedGeneration?.Snapshot;

        if (activeContext is not null && snapshot is null)
        {
            throw new InvalidOperationException(
                "Revisioned conversation context requires a generation snapshot before provider dispatch.");
        }

        if (snapshot is not null
            && snapshot.ContextRevisionId != activeContext?.RevisionId)
        {
            throw new InvalidOperationException(
                "Generation snapshot context revision does not match the active conversation branch context.");
        }

        return new ConversationResponseRequest(
            conversation.Id,
            conversation.Title,
            conversation.Messages,
            snapshot,
            resolvedGeneration?.SystemInstructions);
    }

    private static void ValidateGenerationSnapshot(
        ConversationId conversationId,
        ChatMessage[] messages,
        GenerationSnapshot? generationSnapshot)
    {
        if (generationSnapshot is null)
        {
            return;
        }

        if (generationSnapshot.ConversationId != conversationId)
        {
            throw new ArgumentException(
                "Generation snapshot conversation does not match the response request.",
                nameof(generationSnapshot));
        }

        if (generationSnapshot.InputMessageRevisionIds.Count != messages.Length)
        {
            throw new ArgumentException(
                "Generation snapshot input revisions do not match the response-request messages.",
                nameof(generationSnapshot));
        }

        for (int index = 0; index < messages.Length; index++)
        {
            if (generationSnapshot.InputMessageRevisionIds[index] != messages[index].RevisionId)
            {
                throw new ArgumentException(
                    "Generation snapshot input revisions do not match the response-request messages.",
                    nameof(generationSnapshot));
            }
        }

        ChatMessage triggeringMessage = messages[^1];
        if (triggeringMessage.Role != MessageRole.User
            || triggeringMessage.Id != generationSnapshot.TriggeringUserMessageId
            || triggeringMessage.RevisionId != generationSnapshot.TriggeringUserMessageRevisionId)
        {
            throw new ArgumentException(
                "Generation snapshot trigger does not match the latest user response-request message.",
                nameof(generationSnapshot));
        }
    }
}
