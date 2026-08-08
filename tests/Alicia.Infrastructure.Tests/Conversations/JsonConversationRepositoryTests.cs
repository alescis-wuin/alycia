using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Conversations;

namespace Alicia.Infrastructure.Tests.Conversations;

public sealed class JsonConversationRepositoryTests
{
    [Fact]
    public async Task SaveAsyncAndFindAsyncRoundTripConversation()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            DateTimeOffset createdAt = new(2026, 8, 8, 5, 0, 0, TimeSpan.Zero);
            Conversation conversation = new(
                new ConversationId(Guid.Parse("2f8169cb-4c6e-4b2f-a2ff-e54b9fba1199")),
                createdAt);
            conversation.AddMessage(new ChatMessage(
                new MessageId(Guid.Parse("8fb38fd8-37dd-46fa-a885-e8338324243a")),
                MessageRole.User,
                "Hello Alicia",
                createdAt.AddSeconds(1)));
            conversation.AddMessage(new ChatMessage(
                new MessageId(Guid.Parse("40b40e8f-0cf9-437c-85fd-d73f5781bf77")),
                MessageRole.Assistant,
                "Hello",
                createdAt.AddSeconds(2)));

            await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
            Conversation? loaded = await repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true);

            Conversation persisted = Assert.IsType<Conversation>(loaded);
            Assert.NotSame(conversation, persisted);
            Assert.Equal(conversation.Id, persisted.Id);
            Assert.Equal(conversation.CreatedAt, persisted.CreatedAt);
            Assert.Collection(
                persisted.Messages,
                message => AssertMessage(message, conversation.Messages[0]),
                message => AssertMessage(message, conversation.Messages[1]));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncReturnsNullForUnknownConversation()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);

            Conversation? conversation = await repository
                .FindAsync(ConversationId.New(), CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Null(conversation);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncReturnsDetachedAggregate()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            DateTimeOffset createdAt = new(2026, 8, 8, 5, 10, 0, TimeSpan.Zero);
            Conversation conversation = new(ConversationId.New(), createdAt);
            conversation.AddMessage(new ChatMessage(
                MessageId.New(),
                MessageRole.User,
                "Persisted",
                createdAt));

            await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
            Conversation firstLoad = Assert.IsType<Conversation>(await repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));
            firstLoad.AddMessage(new ChatMessage(
                MessageId.New(),
                MessageRole.Assistant,
                "Not saved",
                createdAt.AddSeconds(1)));

            Conversation secondLoad = Assert.IsType<Conversation>(await repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));

            Assert.Single(secondLoad.Messages);
            Assert.Equal("Persisted", secondLoad.Messages[0].Content);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncRejectsMalformedJson()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            ConversationId conversationId = new(Guid.Parse("0986af6c-eab0-40c0-9d99-42cce716bd11"));
            string filePath = Path.Combine(directory, conversationId.Value.ToString("N") + ".json");
            await File.WriteAllTextAsync(filePath, "{not-json", CancellationToken.None).ConfigureAwait(true);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.FindAsync(conversationId, CancellationToken.None)).ConfigureAwait(true);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void AssertMessage(ChatMessage actual, ChatMessage expected)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Role, actual.Role);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "alicia-infrastructure-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
