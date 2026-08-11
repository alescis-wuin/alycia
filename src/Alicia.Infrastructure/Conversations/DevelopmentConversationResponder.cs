using System.Runtime.CompilerServices;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Conversations;

public sealed class DevelopmentConversationResponder :
    IConversationResponder,
    IStreamingConversationResponder
{
    private const int DefaultChunkSize = 12;
    private static readonly TimeSpan _defaultChunkDelay = TimeSpan.FromMilliseconds(45);

    private readonly TimeSpan _chunkDelay;
    private readonly int _chunkSize;
    private readonly TimeSpan _responseDelay;

    public DevelopmentConversationResponder(TimeSpan responseDelay)
        : this(responseDelay, _defaultChunkDelay, DefaultChunkSize)
    {
    }

    public DevelopmentConversationResponder(
        TimeSpan responseDelay,
        TimeSpan chunkDelay,
        int chunkSize)
    {
        if (responseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseDelay),
                responseDelay,
                "Response delay cannot be negative.");
        }

        if (chunkDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkDelay),
                chunkDelay,
                "Chunk delay cannot be negative.");
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkSize),
                chunkSize,
                "Chunk size must be greater than zero.");
        }

        _responseDelay = responseDelay;
        _chunkDelay = chunkDelay;
        _chunkSize = chunkSize;
    }

    public async Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ConversationResponse response = CreateResponse(request);

        await Task.Delay(_responseDelay, cancellationToken).ConfigureAwait(false);

        return response;
    }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ConversationResponse response = CreateResponse(request);

        await Task.Delay(_responseDelay, cancellationToken).ConfigureAwait(false);

        for (int offset = 0; offset < response.Content.Length; offset += _chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_chunkDelay, cancellationToken).ConfigureAwait(false);

            int chunkLength = Math.Min(_chunkSize, response.Content.Length - offset);
            string contentDelta = response.Content.Substring(offset, chunkLength);
            yield return new ConversationResponseChunk(contentDelta);
        }
    }

    private static ConversationResponse CreateResponse(ConversationResponseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChatMessage? lastUserMessage = request.Messages
            .LastOrDefault(message => message.Role == MessageRole.User);

        if (lastUserMessage is null)
        {
            throw new InvalidOperationException(
                "A development response requires at least one user message.");
        }

        return new ConversationResponse(
            $"Local development response to: {lastUserMessage.Content}");
    }
}
