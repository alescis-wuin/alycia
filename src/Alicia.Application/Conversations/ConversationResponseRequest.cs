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

        if (generationSnapshot is not null
            && generationSnapshot.ConversationId != conversationId)
        {
            throw new ArgumentException(
                "Generation snapshot conversation does not match the response request.",
                nameof(generationSnapshot));
        }

        ChatMessage[] messageSnapshot = messages.ToArray();

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

        return new ConversationResponseRequest(
            conversation.Id,
            conversation.Title,
            conversation.Messages,
            resolvedGeneration?.Snapshot,
            resolvedGeneration?.SystemInstructions);
    }
}
