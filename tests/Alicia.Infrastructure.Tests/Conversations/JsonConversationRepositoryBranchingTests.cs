using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Conversations;

namespace Alicia.Infrastructure.Tests.Conversations;

public sealed class JsonConversationRepositoryBranchingTests
{
    private static readonly DateTimeOffset _createdAt =
        new(2026, 9, 17, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveAsyncAndFindAsyncRoundTripBranchGraph()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            Conversation conversation = CreateBranchedConversation();
            ConversationBranchId activeBranchId = conversation.ActiveBranchId;
            ConversationBranch childBranch = conversation.Branches.Single(
                branch => branch.Id == activeBranchId);
            Assert.True(childBranch.ParentBranchId.HasValue);
            ConversationBranchId originalBranchId = childBranch.ParentBranchId.Value;
            ChatMessage[] originalMessages = conversation.GetMessages(originalBranchId).ToArray();
            ChatMessage[] activeMessages = conversation.Messages.ToArray();

            await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
            Conversation loaded = Assert.IsType<Conversation>(await repository
                .FindAsync(conversation.Id, CancellationToken.None)
                .ConfigureAwait(true));

            Assert.Equal(activeBranchId, loaded.ActiveBranchId);
            Assert.Equal(conversation.MessageRevisions.Count, loaded.MessageRevisions.Count);
            Assert.Equal(2, loaded.Branches.Count);
            AssertMessages(activeMessages, loaded.Messages);
            AssertMessages(originalMessages, loaded.GetMessages(originalBranchId));

            ConversationBranch loadedChild = loaded.Branches.Single(
                branch => branch.Id == activeBranchId);
            Assert.Equal(originalBranchId, loadedChild.ParentBranchId);
            Assert.Equal(childBranch.ForkedAfterRevisionId, loadedChild.ForkedAfterRevisionId);
            Assert.Equal(childBranch.LocalRevisionIds.ToArray(), loadedChild.LocalRevisionIds.ToArray());

            string filePath = Path.Combine(
                directory,
                conversation.Id.Value.ToString("N") + ".json");
            string json = await File.ReadAllTextAsync(
                filePath,
                CancellationToken.None).ConfigureAwait(true);
            Assert.Contains("\"schemaVersion\": 5", json, StringComparison.Ordinal);
            Assert.Contains("\"messageRevisions\"", json, StringComparison.Ordinal);
            Assert.Contains("\"branches\"", json, StringComparison.Ordinal);
            Assert.Contains("\"activeBranchId\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"messages\"", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncReadsSchemaFourIntoStableDeterministicRootBranch()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            ConversationId conversationId = new(Guid.Parse("b69f3bbc-38b7-4f1f-97a1-785bf8c126ae"));
            MessageId messageId = new(Guid.Parse("2f3c6df4-b427-4b69-bc6e-f3fab6df319a"));
            MessageRevisionId revisionId = new(Guid.Parse("a73a2c91-d08a-43c7-ae08-60a3c5a67a0a"));
            GenerationSnapshotId snapshotId = new(Guid.Parse("ad63809a-cb40-4837-b157-60f8f210c1a2"));
            DateTimeOffset messageAt = _createdAt.AddMinutes(1);
            ChatMessage expected = new(
                messageId,
                revisionId,
                parentRevisionId: null,
                MessageRole.Assistant,
                "Schema four",
                messageAt,
                snapshotId);
            string filePath = Path.Combine(directory, conversationId.Value.ToString("N") + ".json");
            string json = $$"""
                {
                  "schemaVersion": 4,
                  "id": "{{conversationId.Value}}",
                  "title": "Schema four",
                  "createdAt": "{{_createdAt:O}}",
                  "updatedAt": "{{messageAt:O}}",
                  "messages": [
                    {
                      "id": "{{messageId.Value}}",
                      "revisionId": "{{revisionId.Value}}",
                      "parentRevisionId": null,
                      "role": 3,
                      "content": "Schema four",
                      "createdAt": "{{messageAt:O}}",
                      "payloadHash": "{{expected.PayloadHash}}",
                      "generationSnapshotId": "{{snapshotId.Value}}"
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(
                filePath,
                json,
                CancellationToken.None).ConfigureAwait(true);

            Conversation first = Assert.IsType<Conversation>(await repository
                .FindAsync(conversationId, CancellationToken.None)
                .ConfigureAwait(true));
            Conversation second = Assert.IsType<Conversation>(await repository
                .FindAsync(conversationId, CancellationToken.None)
                .ConfigureAwait(true));

            Assert.Equal(first.ActiveBranchId, second.ActiveBranchId);
            Assert.False(first.ActiveBranchId.IsEmpty);
            ConversationBranch firstRoot = Assert.Single(first.Branches);
            Assert.Equal(first.ActiveBranchId, firstRoot.Id);
            Assert.Null(firstRoot.ParentBranchId);
            Assert.Equal(new[] { revisionId }, firstRoot.LocalRevisionIds);
            ChatMessage migrated = Assert.Single(first.Messages);
            Assert.Equal(snapshotId, migrated.GenerationSnapshotId);

            await repository.SaveAsync(first, CancellationToken.None).ConfigureAwait(true);
            string upgradedJson = await File.ReadAllTextAsync(
                filePath,
                CancellationToken.None).ConfigureAwait(true);
            Assert.Contains("\"schemaVersion\": 5", upgradedJson, StringComparison.Ordinal);
            Assert.Contains(first.ActiveBranchId.Value.ToString("D"), upgradedJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncRejectsSchemaFiveBranchReferencingUnknownRevision()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            ConversationId conversationId = ConversationId.New();
            ConversationBranchId branchId = ConversationBranchId.New();
            MessageRevisionId unknownRevisionId = MessageRevisionId.New();
            string filePath = Path.Combine(directory, conversationId.Value.ToString("N") + ".json");
            string json = $$"""
                {
                  "schemaVersion": 5,
                  "id": "{{conversationId.Value}}",
                  "title": "Broken graph",
                  "createdAt": "{{_createdAt:O}}",
                  "updatedAt": "{{_createdAt:O}}",
                  "messageRevisions": [],
                  "branches": [
                    {
                      "id": "{{branchId.Value}}",
                      "parentBranchId": null,
                      "forkedAfterRevisionId": null,
                      "localRevisionIds": ["{{unknownRevisionId.Value}}"]
                    }
                  ],
                  "activeBranchId": "{{branchId.Value}}"
                }
                """;
            await File.WriteAllTextAsync(
                filePath,
                json,
                CancellationToken.None).ConfigureAwait(true);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.FindAsync(conversationId, CancellationToken.None)).ConfigureAwait(true);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task FindAsyncRejectsSchemaFiveUnknownActiveBranch()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            JsonConversationRepository repository = new(directory);
            ConversationId conversationId = ConversationId.New();
            ConversationBranchId rootBranchId = ConversationBranchId.New();
            ConversationBranchId missingBranchId = ConversationBranchId.New();
            string filePath = Path.Combine(directory, conversationId.Value.ToString("N") + ".json");
            string json = $$"""
                {
                  "schemaVersion": 5,
                  "id": "{{conversationId.Value}}",
                  "title": "Broken active branch",
                  "createdAt": "{{_createdAt:O}}",
                  "updatedAt": "{{_createdAt:O}}",
                  "messageRevisions": [],
                  "branches": [
                    {
                      "id": "{{rootBranchId.Value}}",
                      "parentBranchId": null,
                      "forkedAfterRevisionId": null,
                      "localRevisionIds": []
                    }
                  ],
                  "activeBranchId": "{{missingBranchId.Value}}"
                }
                """;
            await File.WriteAllTextAsync(
                filePath,
                json,
                CancellationToken.None).ConfigureAwait(true);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.FindAsync(conversationId, CancellationToken.None)).ConfigureAwait(true);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static Conversation CreateBranchedConversation()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "First question",
            _createdAt.AddMinutes(1)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "First answer",
            _createdAt.AddMinutes(2)));
        ChatMessage secondUser = new(
            MessageId.New(),
            MessageRole.User,
            "Second question",
            _createdAt.AddMinutes(3));
        conversation.AddMessage(secondUser);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Second answer",
            _createdAt.AddMinutes(4)));
        conversation.EditUserMessage(
            secondUser.RevisionId,
            "Edited second question",
            _createdAt.AddMinutes(5));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Edited second answer",
            _createdAt.AddMinutes(6)));
        return conversation;
    }

    private static void AssertMessages(
        ChatMessage[] expected,
        IReadOnlyList<ChatMessage> actual)
    {
        Assert.Equal(expected.Length, actual.Count);

        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].RevisionId, actual[index].RevisionId);
            Assert.Equal(expected[index].ParentRevisionId, actual[index].ParentRevisionId);
            Assert.Equal(expected[index].Role, actual[index].Role);
            Assert.Equal(expected[index].Content, actual[index].Content);
            Assert.Equal(expected[index].CreatedAt, actual[index].CreatedAt);
            Assert.Equal(expected[index].GenerationSnapshotId, actual[index].GenerationSnapshotId);
            Assert.Equal(expected[index].PayloadHash, actual[index].PayloadHash);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "alicia-infrastructure-branching-tests",
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
