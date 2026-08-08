using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Conversations;

namespace Alicia.Infrastructure.Tests.Conversations;

public sealed class LocalConversationRuntimeTests
{
    [Fact]
    public async Task RuntimeComposesConversationLifecycleAgainstSharedPersistentRepository()
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
            Conversation renamed = await runtime.RenameConversation
                .ExecuteAsync(
                    conversation.Id,
                    "Runtime lifecycle",
                    CancellationToken.None)
                .ConfigureAwait(true);
            IReadOnlyList<ConversationSummary> summaries = await runtime.ListConversations
                .ExecuteAsync(CancellationToken.None)
                .ConfigureAwait(true);
            Conversation loaded = await runtime.LoadConversation
                .ExecuteAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Equal("Runtime lifecycle", renamed.Title);
            Assert.Equal("Runtime lifecycle", loaded.Title);
            Assert.Equal(appended.Id, Assert.Single(loaded.Messages).Id);
            Assert.Equal(conversation.Id, Assert.Single(summaries).Id);

            await runtime.DeleteConversation
                .ExecuteAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Empty(await runtime.ListConversations
                .ExecuteAsync(CancellationToken.None)
                .ConfigureAwait(true));
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
