using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Conversations;

namespace Alicia.Infrastructure.Tests.Conversations;

public sealed class JsonConversationContextPersistenceTests
{
    private static readonly DateTimeOffset _createdAt =
        new(2026, 9, 18, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SchemaSixRoundTripsContextRevisionsAndBranchInheritance()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            Conversation conversation = new(ConversationId.New(), _createdAt);
            ChatMessage first = new(
                MessageId.New(),
                MessageRole.User,
                "First",
                _createdAt.AddMinutes(1));
            conversation.AddMessage(first);
            ConversationContextRevision parentContext = conversation.UpdateContext(
                "Parent context",
                false,
                _createdAt.AddMinutes(2));
            ChatMessage second = new(
                MessageId.New(),
                MessageRole.User,
                "Second",
                _createdAt.AddMinutes(3));
            conversation.AddMessage(second);
            ConversationBranchId parentBranchId = conversation.ActiveBranchId;
            conversation.EditUserMessage(
                second.RevisionId,
                "Edited second",
                _createdAt.AddMinutes(4));
            ConversationBranchId childBranchId = conversation.ActiveBranchId;
            ConversationContextRevision childContext = conversation.UpdateContext(
                "Child context",
                true,
                _createdAt.AddMinutes(5));

            await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
            Conversation loaded = Assert.IsType<Conversation>(await repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));

            Assert.Equal(2, loaded.ContextRevisions.Count);
            Assert.Equal(childBranchId, loaded.ActiveBranchId);
            Assert.Equal(childContext.RevisionId, loaded.ActiveContextRevision?.RevisionId);
            Assert.Equal(parentContext.RevisionId, loaded.GetContextRevision(parentBranchId)?.RevisionId);
            ConversationBranch child = loaded.Branches.Single(branch => branch.Id == childBranchId);
            Assert.Equal(parentContext.RevisionId, child.InheritedContextRevisionId);
            Assert.Single(child.ContextBindings);

            string path = Path.Combine(directory, conversation.Id.Value.ToString("N") + ".json");
            string json = await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
            Assert.Contains("\"schemaVersion\": 6", json, StringComparison.Ordinal);
            Assert.Contains("\"contextRevisions\"", json, StringComparison.Ordinal);
            Assert.Contains("\"inheritedContextRevisionId\"", json, StringComparison.Ordinal);
            Assert.Contains("\"contextBindings\"", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task SchemaFiveLoadsWithoutInventedContextAndUpgradesToSix()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            ConversationId conversationId = ConversationId.New();
            ConversationBranchId branchId = ConversationBranchId.New();
            string path = Path.Combine(directory, conversationId.Value.ToString("N") + ".json");
            string json = $$"""
                {
                  "schemaVersion": 5,
                  "id": "{{conversationId.Value}}",
                  "title": "Schema five",
                  "createdAt": "{{_createdAt:O}}",
                  "updatedAt": "{{_createdAt:O}}",
                  "messageRevisions": [],
                  "branches": [
                    {
                      "id": "{{branchId.Value}}",
                      "parentBranchId": null,
                      "forkedAfterRevisionId": null,
                      "localRevisionIds": []
                    }
                  ],
                  "activeBranchId": "{{branchId.Value}}"
                }
                """;
            await File.WriteAllTextAsync(path, json, CancellationToken.None).ConfigureAwait(true);

            Conversation loaded = Assert.IsType<Conversation>(await repository
                .FindAsync(conversationId, CancellationToken.None)
                .ConfigureAwait(true));
            Assert.Empty(loaded.ContextRevisions);
            Assert.Null(loaded.ActiveContextRevision);

            await repository.SaveAsync(loaded, CancellationToken.None).ConfigureAwait(true);
            string upgraded = await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
            Assert.Contains("\"schemaVersion\": 6", upgraded, StringComparison.Ordinal);
            Assert.Contains("\"contextRevisions\": []", upgraded, StringComparison.Ordinal);
            Assert.Contains("\"contextBindings\": []", upgraded, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncRejectsTamperedContextRevisionPayloadHash()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            Conversation conversation = new(ConversationId.New(), _createdAt);
            ConversationContextRevision revision = conversation.UpdateContext(
                "Integrity",
                false,
                _createdAt.AddMinutes(1));
            await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
            string path = Path.Combine(directory, conversation.Id.Value.ToString("N") + ".json");
            string json = await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
            string tampered = json.Replace(
                revision.PayloadHash,
                new string('0', 64),
                StringComparison.Ordinal);
            Assert.NotEqual(json, tampered);
            await File.WriteAllTextAsync(path, tampered, CancellationToken.None).ConfigureAwait(true);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.FindAsync(conversation.Id, CancellationToken.None)).ConfigureAwait(true);
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
            "alicia-context-persistence-tests",
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
