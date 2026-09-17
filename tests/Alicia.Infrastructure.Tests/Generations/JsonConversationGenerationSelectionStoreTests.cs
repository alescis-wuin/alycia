using Alicia.Application.Generations;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Generations;

namespace Alicia.Infrastructure.Tests.Generations;

public sealed class JsonConversationGenerationSelectionStoreTests
{
    [Fact]
    public async Task MissingFileReturnsNullAndSavedSelectionSurvivesRestart()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "conversation-selections.json");
        ConversationGenerationSelection selection = CreateSelection(
            ConversationId.New(),
            "provider.alpha",
            "owner/model-a",
            GenerationProfileId.New());

        try
        {
            using (JsonConversationGenerationSelectionStore store = new(path))
            {
                Assert.Null(await store.LoadAsync(
                    selection.ConversationId,
                    TestContext.Current.CancellationToken).ConfigureAwait(true));
                await store.SaveAsync(
                    selection,
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            using JsonConversationGenerationSelectionStore reopened = new(path);
            ConversationGenerationSelection? restored = await reopened.LoadAsync(
                selection.ConversationId,
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(selection, restored);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveReplacesOneConversationWithoutChangingAnother()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "conversation-selections.json");
        ConversationId firstConversationId = ConversationId.New();
        ConversationId secondConversationId = ConversationId.New();
        ConversationGenerationSelection first = CreateSelection(
            firstConversationId,
            "provider.alpha",
            "owner/model-a",
            GenerationProfileId.New());
        ConversationGenerationSelection second = CreateSelection(
            secondConversationId,
            "provider.beta",
            "owner/model-b",
            GenerationProfileId.New());
        ConversationGenerationSelection replacement = CreateSelection(
            firstConversationId,
            "provider.alpha",
            "owner/model-c",
            GenerationProfileId.New());

        try
        {
            using JsonConversationGenerationSelectionStore store = new(path);
            await store.SaveAsync(first, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await store.SaveAsync(second, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await store.SaveAsync(replacement, TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(
                replacement,
                await store.LoadAsync(
                    firstConversationId,
                    TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal(
                second,
                await store.LoadAsync(
                    secondConversationId,
                    TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteRemovesOnlyRequestedConversationSelection()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "conversation-selections.json");
        ConversationGenerationSelection first = CreateSelection(
            ConversationId.New(),
            "provider.alpha",
            "owner/model-a",
            GenerationProfileId.New());
        ConversationGenerationSelection second = CreateSelection(
            ConversationId.New(),
            "provider.alpha",
            "owner/model-b",
            GenerationProfileId.New());

        try
        {
            using JsonConversationGenerationSelectionStore store = new(path);
            await store.SaveAsync(first, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await store.SaveAsync(second, TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.True(await store.DeleteAsync(
                first.ConversationId,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Null(await store.LoadAsync(
                first.ConversationId,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal(
                second,
                await store.LoadAsync(
                    second.ConversationId,
                    TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.False(await store.DeleteAsync(
                first.ConversationId,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadRejectsDuplicateConversationEntries()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "conversation-selections.json");
        Guid conversationId = Guid.NewGuid();
        string json = $$"""
        {
          "schemaVersion": 1,
          "selections": [
            {
              "conversationId": "{{conversationId:D}}",
              "providerId": "provider.alpha",
              "modelReference": "owner/model-a",
              "profileId": "{{Guid.NewGuid():D}}"
            },
            {
              "conversationId": "{{conversationId:D}}",
              "providerId": "provider.alpha",
              "modelReference": "owner/model-b",
              "profileId": "{{Guid.NewGuid():D}}"
            }
          ]
        }
        """;

        try
        {
            await File.WriteAllTextAsync(
                path,
                json,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            using JsonConversationGenerationSelectionStore store = new(path);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(
                new ConversationId(conversationId),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadRejectsUnsupportedSchemaAndMalformedSelection()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "conversation-selections.json");

        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 99,
                  "selections": []
                }
                """,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            using (JsonConversationGenerationSelectionStore unsupportedStore = new(path))
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => unsupportedStore.LoadAsync(
                    ConversationId.New(),
                    TestContext.Current.CancellationToken)).ConfigureAwait(true);
            }

            await File.WriteAllTextAsync(
                path,
                $$"""
                {
                  "schemaVersion": 1,
                  "selections": [
                    {
                      "conversationId": "{{Guid.NewGuid():D}}",
                      "providerId": " ",
                      "modelReference": "owner/model",
                      "profileId": "{{Guid.NewGuid():D}}"
                    }
                  ]
                }
                """,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            using JsonConversationGenerationSelectionStore malformedStore = new(path);

            await Assert.ThrowsAsync<InvalidDataException>(() => malformedStore.LoadAsync(
                ConversationId.New(),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ConversationGenerationSelection CreateSelection(
        ConversationId conversationId,
        string providerId,
        string modelReference,
        GenerationProfileId profileId)
    {
        return new ConversationGenerationSelection(
            conversationId,
            new GenerationProfileModelScope(providerId, modelReference),
            profileId);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-generation-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
