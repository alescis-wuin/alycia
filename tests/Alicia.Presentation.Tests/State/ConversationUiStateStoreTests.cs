using Alicia.Domain.Conversations;
using Alicia.Presentation.State;

namespace Alicia.Presentation.Tests.State;

public sealed class ConversationUiStateStoreTests
{
    [Fact]
    public async Task JsonStoreRoundTripsHistoryAndPerConversationScrollState()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-ui-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "ui-state.json");
        ConversationId conversationId = ConversationId.New();
        using JsonConversationUiStateStore store = new(path);
        ConversationUiStateSnapshot snapshot = ConversationUiStateSnapshot.Default
            .WithHistoryExpanded(isExpanded: false)
            .WithConversationScrollState(
                conversationId,
                new ConversationScrollState(
                    ConversationScrollMode.Detached,
                    verticalOffset: 137.5));

        try
        {
            await store
                .SaveAsync(snapshot, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            ConversationUiStateSnapshot loaded = await store
                .LoadAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.False(loaded.IsConversationHistoryExpanded);
            ConversationScrollState scrollState = loaded.GetConversationScrollState(conversationId);
            Assert.Equal(ConversationScrollMode.Detached, scrollState.Mode);
            Assert.Equal(137.5, scrollState.VerticalOffset);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JsonStoreFallsBackSafelyWhenDocumentIsMalformed()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-ui-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "ui-state.json");
        Directory.CreateDirectory(directory);
        await File
            .WriteAllTextAsync(
                path,
                "{not-json",
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        using JsonConversationUiStateStore store = new(path);

        try
        {
            ConversationUiStateSnapshot loaded = await store
                .LoadAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.True(loaded.IsConversationHistoryExpanded);
            Assert.Empty(loaded.ConversationScrollStates);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStoreIgnoresInvalidConversationEntriesWithoutDiscardingValidState()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-ui-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "ui-state.json");
        Directory.CreateDirectory(directory);
        ConversationId validId = ConversationId.New();
        string json = $$"""
            {
              "version": 1,
              "isConversationHistoryExpanded": false,
              "conversations": {
                "{{validId}}": { "mode": "Detached", "verticalOffset": 88 },
                "not-a-guid": { "mode": "Detached", "verticalOffset": 12 },
                "00000000-0000-0000-0000-000000000000": { "mode": "Following", "verticalOffset": 0 },
                "11111111-1111-1111-1111-111111111111": null
              }
            }
            """;
        await File
            .WriteAllTextAsync(
                path,
                json,
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        using JsonConversationUiStateStore store = new(path);

        try
        {
            ConversationUiStateSnapshot loaded = await store
                .LoadAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.False(loaded.IsConversationHistoryExpanded);
            Assert.Single(loaded.ConversationScrollStates);
            Assert.Equal(
                88d,
                loaded.GetConversationScrollState(validId).VerticalOffset);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
