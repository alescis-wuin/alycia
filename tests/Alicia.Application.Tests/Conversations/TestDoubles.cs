using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

internal sealed class InMemoryConversationRepository : IConversationRepository
{
    private readonly Dictionary<ConversationId, Conversation> _conversations = [];

    public int SaveCount { get; private set; }

    public Task<Conversation?> FindAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _conversations.TryGetValue(conversationId, out Conversation? conversation);
        return Task.FromResult(conversation);
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
