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

internal sealed class DeterministicConversationResponder : IConversationResponder
{
    private readonly ConversationResponse _response;

    public DeterministicConversationResponder(string responseContent)
    {
        _response = new ConversationResponse(responseContent);
    }

    public int CallCount { get; private set; }

    public ConversationResponseRequest? LastRequest { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastRequest = request;
        LastCancellationToken = cancellationToken;

        return Task.FromResult(_response);
    }
}

internal sealed class FailingConversationResponder : IConversationResponder
{
    private readonly Exception _exception;

    public FailingConversationResponder(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _exception = exception;
    }

    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromException<ConversationResponse>(_exception);
    }
}

internal sealed class NullConversationResponder : IConversationResponder
{
    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<ConversationResponse>(null!);
    }
}


internal sealed class DelegateConversationResponder : IConversationResponder
{
    private readonly Func<ConversationResponseRequest, CancellationToken, Task<ConversationResponse>> _generate;

    public DelegateConversationResponder(
        Func<ConversationResponseRequest, CancellationToken, Task<ConversationResponse>> generate)
    {
        ArgumentNullException.ThrowIfNull(generate);
        _generate = generate;
    }

    public int CallCount { get; private set; }

    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CallCount++;
        return _generate(request, cancellationToken);
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
