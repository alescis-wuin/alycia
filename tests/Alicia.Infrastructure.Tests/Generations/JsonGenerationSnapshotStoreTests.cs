using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Generations;

namespace Alicia.Infrastructure.Tests.Generations;

public sealed class JsonGenerationSnapshotStoreTests
{
    [Fact]
    public async Task SaveAsyncAndFindAsyncRoundTripImmutableSnapshot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string snapshotsDirectory = Path.Combine(directory.Path, "snapshots");
        JsonGenerationSnapshotStore store = new(snapshotsDirectory);
        GenerationSnapshot snapshot = CreateSnapshot();

        await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(true);
        GenerationSnapshot? loaded = await store
            .FindAsync(snapshot.Id, cancellationToken)
            .ConfigureAwait(true);

        GenerationSnapshot persisted = Assert.IsType<GenerationSnapshot>(loaded);
        Assert.NotSame(snapshot, persisted);
        Assert.Equal(snapshot.Id, persisted.Id);
        Assert.Equal(snapshot.ConversationId, persisted.ConversationId);
        Assert.Equal(snapshot.TriggeringUserMessageId, persisted.TriggeringUserMessageId);
        Assert.Equal(
            snapshot.TriggeringUserMessageRevisionId,
            persisted.TriggeringUserMessageRevisionId);
        Assert.Equal(snapshot.InputMessageRevisionIds, persisted.InputMessageRevisionIds);
        Assert.Equal(snapshot.CapturedAtUtc, persisted.CapturedAtUtc);
        Assert.Equal(snapshot.ProviderId, persisted.ProviderId);
        Assert.Equal(snapshot.ModelReference, persisted.ModelReference);
        Assert.Equal(snapshot.ContextSize, persisted.ContextSize);
        Assert.Equal(snapshot.GenerationOptions, persisted.GenerationOptions);
        Assert.Equal(snapshot.ProfileId, persisted.ProfileId);
        Assert.Equal(snapshot.ProfileRevisionId, persisted.ProfileRevisionId);
        Assert.Equal(snapshot.PayloadHash, persisted.PayloadHash);

        string expectedPath = Path.Combine(
            snapshotsDirectory,
            snapshot.Id.Value.ToString("N") + ".json");
        string json = await File.ReadAllTextAsync(expectedPath, cancellationToken)
            .ConfigureAwait(true);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains(snapshot.PayloadHash, json, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(
            snapshotsDirectory,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SaveAsyncRefusesToOverwriteExistingSnapshot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        JsonGenerationSnapshotStore store = new(directory.Path);
        GenerationSnapshot snapshot = CreateSnapshot();

        await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(snapshot, cancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task FindAsyncRejectsTamperedPayloadHash()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        JsonGenerationSnapshotStore store = new(directory.Path);
        GenerationSnapshot snapshot = CreateSnapshot();
        await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(true);
        string path = Path.Combine(
            directory.Path,
            snapshot.Id.Value.ToString("N") + ".json");
        string json = await File.ReadAllTextAsync(path, cancellationToken)
            .ConfigureAwait(true);
        string tampered = json.Replace(
            snapshot.PayloadHash,
            new string('0', snapshot.PayloadHash.Length),
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        await File.WriteAllTextAsync(path, tampered, cancellationToken)
            .ConfigureAwait(true);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.FindAsync(snapshot.Id, cancellationToken)).ConfigureAwait(true);

        Assert.Contains("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindAsyncRejectsIdentifierMismatch()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        JsonGenerationSnapshotStore store = new(directory.Path);
        GenerationSnapshot snapshot = CreateSnapshot();
        await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(true);
        GenerationSnapshotId requestedId = GenerationSnapshotId.New();
        string sourcePath = Path.Combine(
            directory.Path,
            snapshot.Id.Value.ToString("N") + ".json");
        string requestedPath = Path.Combine(
            directory.Path,
            requestedId.Value.ToString("N") + ".json");
        File.Copy(sourcePath, requestedPath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.FindAsync(requestedId, cancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeleteAsyncRemovesSnapshotDocument()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        JsonGenerationSnapshotStore store = new(directory.Path);
        GenerationSnapshot snapshot = CreateSnapshot();
        await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(true);

        Assert.True(await store.DeleteAsync(snapshot.Id, cancellationToken).ConfigureAwait(true));
        Assert.False(await store.DeleteAsync(snapshot.Id, cancellationToken).ConfigureAwait(true));
        Assert.Null(await store.FindAsync(snapshot.Id, cancellationToken).ConfigureAwait(true));
    }

    private static GenerationSnapshot CreateSnapshot()
    {
        MessageRevisionId firstRevisionId = new(
            Guid.Parse("c4eedab6-9b69-4ec5-b86a-d7b7264db534"));
        MessageRevisionId triggerRevisionId = new(
            Guid.Parse("bb558081-4fe2-427f-bd38-9e47709f6913"));

        return new GenerationSnapshot(
            new GenerationSnapshotId(Guid.Parse("ba588512-f90e-4a0b-a589-ab30f859bb42")),
            new ConversationId(Guid.Parse("7311aab8-a717-4cb2-b065-64a76d503eca")),
            new MessageId(Guid.Parse("4f658551-3f4f-4d63-af4b-4c77a07ddb5b")),
            triggerRevisionId,
            new[] { firstRevisionId, triggerRevisionId },
            new DateTimeOffset(2026, 9, 17, 9, 15, 0, TimeSpan.Zero),
            new InferenceProviderConfiguration(
                "provider.alpha",
                "owner/model:Q4_K_M",
                contextSize: 8192,
                generation: new InferenceGenerationOptions(
                    maxOutputTokens: 768,
                    temperature: 0.35,
                    topP: 0.9,
                    topK: 40,
                    seed: 11,
                    reasoningEnabled: true,
                    reasoningBudgetTokens: 256)),
            new GenerationProfileId(Guid.Parse("828b1f1a-23c3-4c8b-8900-72e4a5582fe0")),
            new GenerationProfileRevisionId(Guid.Parse("ac1fc917-2321-4139-abd0-e73b1005b037")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"alicia-generation-snapshot-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
