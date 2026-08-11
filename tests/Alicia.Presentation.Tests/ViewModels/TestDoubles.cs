using System.Runtime.CompilerServices;
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

internal sealed class DeterministicConversationResponder :
    IConversationResponder,
    IStreamingConversationResponder
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

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        await Task.Yield();
        yield return new ConversationResponseChunk(_response.Content);
    }
}

internal sealed class CancellableConversationResponder :
    IConversationResponder,
    IStreamingConversationResponder
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

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _started.TrySetResult();

        await Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);

        yield break;
    }
}

internal sealed class PausingStreamingConversationResponder : IStreamingConversationResponder
{
    private readonly TaskCompletionSource _firstChunkObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConversationResponseChunk _firstChunk;
    private readonly ConversationResponseChunk _secondChunk;

    public PausingStreamingConversationResponder(
        string firstContentDelta,
        string secondContentDelta)
    {
        _firstChunk = new ConversationResponseChunk(firstContentDelta);
        _secondChunk = new ConversationResponseChunk(secondContentDelta);
    }

    public Task FirstChunkObserved => _firstChunkObserved.Task;

    public int CallCount { get; private set; }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        yield return _firstChunk;
        _firstChunkObserved.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        yield return _secondChunk;
    }

    public void Release()
    {
        _release.TrySetResult();
    }
}

internal sealed class FailOnceConversationResponder :
    IConversationResponder,
    IStreamingConversationResponder
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

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;

        if (CallCount == 1)
        {
            yield return new ConversationResponseChunk("Discarded partial response");
            await Task.Yield();
            throw new InvalidOperationException("Development responder failed.");
        }

        await Task.Yield();
        yield return new ConversationResponseChunk(_response.Content);
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
