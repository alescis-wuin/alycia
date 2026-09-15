using Alicia.Application.Conversations;
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
            DateTimeOffset renamedAt = createdAt.AddMinutes(1);
            Conversation conversation = new(
                new ConversationId(Guid.Parse("2f8169cb-4c6e-4b2f-a2ff-e54b9fba1199")),
                createdAt);
            conversation.Rename("Persistence design", renamedAt);
            conversation.AddMessage(new ChatMessage(
                new MessageId(Guid.Parse("8fb38fd8-37dd-46fa-a885-e8338324243a")),
                MessageRole.User,
                "Hello Alicia",
                renamedAt.AddSeconds(1)));
            conversation.AddMessage(new ChatMessage(
                new MessageId(Guid.Parse("40b40e8f-0cf9-437c-85fd-d73f5781bf77")),
                MessageRole.Assistant,
                "Hello",
                renamedAt.AddSeconds(2)));

            await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
            Conversation? loaded = await repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true);

            Conversation persisted = Assert.IsType<Conversation>(loaded);
            Assert.NotSame(conversation, persisted);
            Assert.Equal(conversation.Id, persisted.Id);
            Assert.Equal(conversation.Title, persisted.Title);
            Assert.Equal(conversation.CreatedAt, persisted.CreatedAt);
            Assert.Equal(conversation.UpdatedAt, persisted.UpdatedAt);
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
    public async Task ListAsyncReturnsConversationSummariesByRecentActivity()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            DateTimeOffset createdAt = new(2026, 8, 8, 5, 20, 0, TimeSpan.Zero);
            Conversation older = new(ConversationId.New(), createdAt);
            Conversation newer = new(ConversationId.New(), createdAt.AddMinutes(1));
            older.Rename("Recently changed", createdAt.AddMinutes(2));

            await repository.SaveAsync(newer, CancellationToken.None).ConfigureAwait(true);
            await repository.SaveAsync(older, CancellationToken.None).ConfigureAwait(true);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "settings.json"),
                "{}",
                CancellationToken.None).ConfigureAwait(true);

            IReadOnlyList<ConversationSummary> summaries = await repository
                .ListAsync(CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Collection(
                summaries,
                summary =>
                {
                    Assert.Equal(older.Id, summary.Id);
                    Assert.Equal("Recently changed", summary.Title);
                    Assert.Equal(0, summary.MessageCount);
                },
                summary => Assert.Equal(newer.Id, summary.Id));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteAsyncRemovesConversationDocument()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            Conversation conversation = new(
                ConversationId.New(),
                new DateTimeOffset(2026, 8, 8, 5, 30, 0, TimeSpan.Zero));

            await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);

            Assert.True(await repository
                .DeleteAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));
            Assert.False(await repository
                .DeleteAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));
            Assert.Null(await repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncReadsLegacyConversationDocument()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            ConversationId conversationId = new(Guid.Parse("c6dbd71a-4e40-4055-82e6-aa8e0ce219b6"));
            MessageId messageId = new(Guid.Parse("106b4234-a474-42ef-8f4f-1478ec3246f3"));
            DateTimeOffset createdAt = new(2026, 8, 8, 5, 40, 0, TimeSpan.Zero);
            DateTimeOffset messageAt = createdAt.AddMinutes(2);
            string filePath = Path.Combine(directory, conversationId.Value.ToString("N") + ".json");
            string legacyJson = $$"""
                {
                  "id": "{{conversationId.Value}}",
                  "createdAt": "{{createdAt:O}}",
                  "messages": [
                    {
                      "id": "{{messageId.Value}}",
                      "role": 2,
                      "content": "Legacy message",
                      "createdAt": "{{messageAt:O}}"
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(
                filePath,
                legacyJson,
                CancellationToken.None).ConfigureAwait(true);

            Conversation loaded = Assert.IsType<Conversation>(await repository
                .FindAsync(conversationId, CancellationToken.None)
                .ConfigureAwait(true));

            Assert.Equal(Conversation.DefaultTitle, loaded.Title);
            Assert.Equal(messageAt, loaded.UpdatedAt);
            Assert.Single(loaded.Messages);
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

    [Fact]
    public async Task FindAsyncRejectsIdentifierMismatch()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            ConversationId expectedId = new(Guid.Parse("25bbc39c-4f4b-4bda-a2c4-84631bb1ab49"));
            ConversationId documentId = new(Guid.Parse("a66ef2cc-f260-4d50-abf8-a9371a112f4f"));
            string filePath = Path.Combine(directory, expectedId.Value.ToString("N") + ".json");
            string json = $$"""
                {
                  "schemaVersion": 2,
                  "id": "{{documentId.Value}}",
                  "title": "Wrong document",
                  "createdAt": "2026-08-08T05:50:00+00:00",
                  "updatedAt": "2026-08-08T05:50:00+00:00",
                  "messages": []
                }
                """;
            await File.WriteAllTextAsync(filePath, json, CancellationToken.None).ConfigureAwait(true);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.FindAsync(expectedId, CancellationToken.None)).ConfigureAwait(true);
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
