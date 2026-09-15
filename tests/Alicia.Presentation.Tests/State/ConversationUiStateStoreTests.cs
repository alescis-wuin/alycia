using Alicia.Domain.Conversations;
using Alicia.Presentation.State;

namespace Alicia.Presentation.Tests.State;

public sealed class ConversationUiStateStoreTests
{
    [Fact]
    public async Task JsonStoreRoundTripsHistoryScrollAndConversationIdentity()
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
                    verticalOffset: 137.5))
            .WithConversationIdentity(
                conversationId,
                new ConversationVisualIdentity(
                    ConversationIdentityIcon.Code,
                    ConversationIdentityColor.Violet));

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
            ConversationVisualIdentity identity = loaded.GetConversationIdentity(conversationId);
            Assert.Equal(ConversationIdentityIcon.Code, identity.Icon);
            Assert.Equal(ConversationIdentityColor.Violet, identity.Color);
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
            Assert.Empty(loaded.ConversationIdentities);
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
            Assert.Empty(loaded.ConversationIdentities);
            Assert.Equal(
                ConversationVisualIdentity.Default,
                loaded.GetConversationIdentity(validId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStoreIgnoresInvalidIdentitiesWithoutDiscardingValidEntries()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-ui-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "ui-state.json");
        Directory.CreateDirectory(directory);
        ConversationId validId = ConversationId.New();
        ConversationId invalidIconId = ConversationId.New();
        ConversationId invalidColorId = ConversationId.New();
        string json = $$"""
            {
              "version": 2,
              "isConversationHistoryExpanded": true,
              "conversations": {},
              "identities": {
                "{{validId}}": { "icon": "Research", "color": "Orange" },
                "{{invalidIconId}}": { "icon": "Unknown", "color": "Blue" },
                "{{invalidColorId}}": { "icon": "Study", "color": "Invisible" },
                "not-a-guid": { "icon": "Code", "color": "Violet" }
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

            KeyValuePair<string, ConversationVisualIdentity> entry = Assert.Single(
                loaded.ConversationIdentities);
            Assert.Equal(validId.ToString(), entry.Key);
            Assert.Equal(ConversationIdentityIcon.Research, entry.Value.Icon);
            Assert.Equal(ConversationIdentityColor.Orange, entry.Value.Color);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
