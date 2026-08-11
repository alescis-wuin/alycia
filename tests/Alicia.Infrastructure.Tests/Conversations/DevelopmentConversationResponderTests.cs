using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Conversations;

namespace Alicia.Infrastructure.Tests.Conversations;

public sealed class DevelopmentConversationResponderTests
{
    [Fact]
    public async Task GenerateAsyncReturnsDeterministicResponseForLastUserMessage()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 0, 0, TimeSpan.Zero);
        ConversationId conversationId = ConversationId.New();
        ChatMessage systemMessage = new(
            MessageId.New(),
            MessageRole.System,
            "System",
            createdAt);
        ChatMessage firstUserMessage = new(
            MessageId.New(),
            MessageRole.User,
            "First",
            createdAt.AddMinutes(1));
        ChatMessage lastUserMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Second",
            createdAt.AddMinutes(2));
        ChatMessage[] messages =
        [
            systemMessage,
            firstUserMessage,
            lastUserMessage,
        ];
        ConversationResponseRequest request = new(
            conversationId,
            "Project",
            messages);
        DevelopmentConversationResponder responder = new(TimeSpan.Zero);

        ConversationResponse response = await responder.GenerateAsync(
            request,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
            "Local development response to: Second",
            response.Content);
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
    public async Task GenerateAsyncHonorsCancellation()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 16, 20, 0, TimeSpan.Zero);
        ChatMessage[] messages =
        [
            new ChatMessage(
                MessageId.New(),
                MessageRole.User,
                "Cancel",
                createdAt),
        ];
        ConversationResponseRequest request = new(
            ConversationId.New(),
            "Project",
            messages);
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
    public void ConstructorRejectsNegativeDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DevelopmentConversationResponder(
                TimeSpan.FromMilliseconds(-1)));
    }
}
