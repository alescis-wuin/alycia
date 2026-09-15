using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Conversations;

namespace Alicia.Infrastructure.Tests.Conversations;

public sealed class DevelopmentConversationResponderTests
{
    [Fact]
    public async Task GenerateAsyncReturnsDeterministicResponseForLastUserMessage()
    {
        ConversationResponseRequest request = CreateRequest("First", "Second");
        DevelopmentConversationResponder responder = new(TimeSpan.Zero);

        ConversationResponse response = await responder.GenerateAsync(
            request,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
            "Local development response to: Second",
            response.Content);
    }

    [Fact]
    public async Task StreamAsyncReconstructsSameDeterministicResponseAcrossChunks()
    {
        ConversationResponseRequest request = CreateRequest("First", "Second");
        DevelopmentConversationResponder responder = new(
            TimeSpan.Zero,
            TimeSpan.Zero,
            chunkSize: 7);
        List<ConversationResponseChunk> chunks = [];

        await foreach (ConversationResponseChunk chunk in responder
            .StreamAsync(request, CancellationToken.None)
            .ConfigureAwait(true))
        {
            chunks.Add(chunk);
        }

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.NotEmpty(chunk.ContentDelta));
        Assert.Equal(
            "Local development response to: Second",
            string.Concat(chunks.Select(chunk => chunk.ContentDelta)));
    }

    [Fact]
    public async Task GenerateAsyncRejectsRequestWithoutUserMessage()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 10, 0, TimeSpan.Zero);
        ChatMessage[] messages =
        [
            new ChatMessage(
                MessageId.New(),
                MessageRole.System,
                "System",
                createdAt),
        ];
        ConversationResponseRequest request = new(
            ConversationId.New(),
            "Project",
            messages);
        DevelopmentConversationResponder responder = new(TimeSpan.Zero);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                responder.GenerateAsync(
                    request,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "requires at least one user message",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsyncRejectsRequestWithoutUserMessage()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 15, 0, TimeSpan.Zero);
        ChatMessage[] messages =
        [
            new ChatMessage(
                MessageId.New(),
                MessageRole.System,
                "System",
                createdAt),
        ];
        ConversationResponseRequest request = new(
            ConversationId.New(),
            "Project",
            messages);
        DevelopmentConversationResponder responder = new(TimeSpan.Zero);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConsumeAsync(responder.StreamAsync(
                request,
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Contains(
            "requires at least one user message",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsyncHonorsCancellation()
    {
        ConversationResponseRequest request = CreateRequest("Cancel");
        DevelopmentConversationResponder responder =
            new(TimeSpan.FromSeconds(1));
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            responder.GenerateAsync(
                request,
                cancellationSource.Token)).ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamAsyncHonorsCancellationBetweenChunks()
    {
        ConversationResponseRequest request = CreateRequest("Cancel stream");
        DevelopmentConversationResponder responder = new(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            chunkSize: 4);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ConsumeAsync(responder.StreamAsync(
                request,
                cancellationSource.Token))).ConfigureAwait(true);
    }

    [Fact]
    public void ConstructorRejectsInvalidTimingAndChunkSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DevelopmentConversationResponder(
                TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DevelopmentConversationResponder(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(-1),
                chunkSize: 8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DevelopmentConversationResponder(
                TimeSpan.Zero,
                TimeSpan.Zero,
                chunkSize: 0));
    }

    private static ConversationResponseRequest CreateRequest(params string[] userMessages)
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 0, 0, TimeSpan.Zero);
        ConversationId conversationId = ConversationId.New();
        List<ChatMessage> messages =
        [
            new ChatMessage(
                MessageId.New(),
                MessageRole.System,
                "System",
                createdAt),
        ];

        for (int index = 0; index < userMessages.Length; index++)
        {
            messages.Add(new ChatMessage(
                MessageId.New(),
                MessageRole.User,
                userMessages[index],
                createdAt.AddMinutes(index + 1)));
        }

        return new ConversationResponseRequest(
            conversationId,
            "Project",
            messages);
    }

    private static async Task ConsumeAsync(
        IAsyncEnumerable<ConversationResponseChunk> stream)
    {
        await foreach (ConversationResponseChunk _ in stream.ConfigureAwait(true))
        {
        }
    }
}
