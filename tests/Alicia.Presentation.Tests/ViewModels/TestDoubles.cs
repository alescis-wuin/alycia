using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Presentation.Tests.ViewModels;

internal sealed class InMemoryConversationRepository : IConversationRepository
{
    private readonly Dictionary<ConversationId, Conversation> _conversations = [];

    public Exception? SaveException { get; set; }

    public Task<bool> DeleteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_conversations.Remove(conversationId));
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

        IReadOnlyList<ConversationSummary> summaries = _conversations.Values
            .Select(ConversationSummary.FromConversation)
            .OrderByDescending(summary => summary.UpdatedAt)
            .ThenByDescending(summary => summary.CreatedAt)
            .ThenBy(summary => summary.Id.Value)
            .ToArray();

        return Task.FromResult(summaries);
    }

    public Task SaveAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();

        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }

        _conversations[conversation.Id] = conversation;
        return Task.CompletedTask;
    }

    public void Seed(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _conversations[conversation.Id] = conversation;
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

    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        return Task.FromResult(_response);
    }
}

internal sealed class CancellableConversationResponder : IConversationResponder
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public async Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _started.TrySetResult();

        await Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);

        return new ConversationResponse("Unreachable response");
    }
}

internal sealed class FailOnceConversationResponder : IConversationResponder
{
    private readonly ConversationResponse _response;

    public FailOnceConversationResponder(string responseContent)
    {
        _response = new ConversationResponse(responseContent);
    }

    public int CallCount { get; private set; }

    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;

        return CallCount == 1
            ? Task.FromException<ConversationResponse>(
                new InvalidOperationException("Development responder failed."))
            : Task.FromResult(_response);
    }
}

internal sealed class MutableTimeProvider : TimeProvider
{
    public MutableTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }
}
