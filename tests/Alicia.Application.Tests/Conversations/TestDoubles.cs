using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

internal sealed class InMemoryConversationRepository : IConversationRepository
{
    private readonly Dictionary<ConversationId, Conversation> _conversations = [];

    public int DeleteCount { get; private set; }

    public int SaveCount { get; private set; }

    public Task<bool> DeleteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool deleted = _conversations.Remove(conversationId);

        if (deleted)
        {
            DeleteCount++;
        }

        return Task.FromResult(deleted);
    }

    public Task<Conversation?> FindAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _conversations.TryGetValue(conversationId, out Conversation? conversation);
        return Task.FromResult(conversation);
    }

    public Task<IReadOnlyList<ConversationSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ConversationSummary> conversations = _conversations.Values
            .Select(ConversationSummary.FromConversation)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.CreatedAt)
            .ThenBy(conversation => conversation.Id.Value)
            .ToArray();

        return Task.FromResult(conversations);
    }

    public Task SaveAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();

        _conversations[conversation.Id] = conversation;
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }
}
