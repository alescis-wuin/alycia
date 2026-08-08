using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Conversations;

namespace Alicia.Infrastructure.Tests.Conversations;

public sealed class LocalConversationRuntimeTests
{
    [Fact]
    public async Task RuntimeComposesUseCasesAgainstSharedPersistentRepository()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            DateTimeOffset now = new(2026, 8, 8, 5, 30, 0, TimeSpan.Zero);
            LocalConversationRuntime runtime = LocalConversationRuntime.Create(
                directory,
                new FixedTimeProvider(now));

            Conversation conversation = await runtime.CreateConversation
                .ExecuteAsync(CancellationToken.None)
                .ConfigureAwait(true);
            ChatMessage appended = await runtime.AppendMessage
                .ExecuteAsync(
                    conversation.Id,
                    MessageRole.User,
                    "Persist this message",
                    CancellationToken.None)
                .ConfigureAwait(true);

            LocalConversationRuntime reloadedRuntime = LocalConversationRuntime.Create(directory);
            Conversation persisted = Assert.IsType<Conversation>(await reloadedRuntime.Repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));

            ChatMessage persistedMessage = Assert.Single(persisted.Messages);
            Assert.Equal(appended.Id, persistedMessage.Id);
            Assert.Equal("Persist this message", persistedMessage.Content);
            Assert.Equal(now, persistedMessage.CreatedAt);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "alicia-runtime-tests",
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

    private sealed class FixedTimeProvider : TimeProvider
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
}
