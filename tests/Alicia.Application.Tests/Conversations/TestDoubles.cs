using System.Runtime.CompilerServices;
using Alicia.Application.Conversations;
using Alicia.Application.Generations;
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

internal sealed class DeterministicStreamingConversationResponder : IStreamingConversationResponder
{
    private readonly ConversationResponseChunk[] _chunks;

    public DeterministicStreamingConversationResponder(params string[] contentDeltas)
    {
        ArgumentNullException.ThrowIfNull(contentDeltas);
        _chunks = contentDeltas
            .Select(contentDelta => new ConversationResponseChunk(contentDelta))
            .ToArray();
    }

    public int CallCount { get; private set; }

    public ConversationResponseRequest? LastRequest { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastRequest = request;
        LastCancellationToken = cancellationToken;

        foreach (ConversationResponseChunk chunk in _chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }
}

internal sealed class ReasoningStreamingConversationResponder : IStreamingConversationResponder
{
    private readonly ConversationResponseChunk[] _chunks;

    public ReasoningStreamingConversationResponder(
        params ConversationResponseChunk[] chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        _chunks = chunks;
    }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (ConversationResponseChunk chunk in _chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
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

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

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

internal sealed class FailingAfterFirstStreamingConversationResponder : IStreamingConversationResponder
{
    private readonly Exception _exception;
    private readonly ConversationResponseChunk _firstChunk;

    public FailingAfterFirstStreamingConversationResponder(
        string firstContentDelta,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _firstChunk = new ConversationResponseChunk(firstContentDelta);
        _exception = exception;
    }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        yield return _firstChunk;
        await Task.Yield();
        throw _exception;
    }
}


internal sealed class InMemoryGenerationSnapshotStore : IGenerationSnapshotStore
{
    private readonly Dictionary<GenerationSnapshotId, GenerationSnapshot> _snapshots = [];

    public int SaveCount { get; private set; }

    public int DeleteCount { get; private set; }

    public IReadOnlyCollection<GenerationSnapshot> Snapshots => _snapshots.Values;

    public Task<GenerationSnapshot?> FindAsync(
        GenerationSnapshotId snapshotId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _snapshots.TryGetValue(snapshotId, out GenerationSnapshot? snapshot);
        return Task.FromResult(snapshot);
    }

    public Task SaveAsync(
        GenerationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_snapshots.TryAdd(snapshot.Id, snapshot))
        {
            throw new InvalidOperationException(
                $"Generation snapshot '{snapshot.Id}' is already stored.");
        }

        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(
        GenerationSnapshotId snapshotId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool deleted = _snapshots.Remove(snapshotId);
        if (deleted)
        {
            DeleteCount++;
        }

        return Task.FromResult(deleted);
    }
}
