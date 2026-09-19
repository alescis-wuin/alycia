using Alicia.Application.Providers;
using Alicia.Infrastructure.Providers;

namespace Alicia.Infrastructure.Tests.Providers;

public sealed class JsonInferenceModelLibraryStoreTests
{
    [Fact]
    public async Task MissingLibraryReturnsNullWithoutCreatingFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "providers", "models.json");
        using JsonInferenceModelLibraryStore store = new(path);

        InferenceModelLibrary? library = await store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);

        Assert.Null(library);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task StoreRoundTripsMultipleModelConfigurationsWithoutSecrets()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "providers", "models.json");
        using JsonInferenceModelLibraryStore store = new(path);
        InferenceModelLibrary library = new(
        [
            CreateEntry(
                "llama.cpp.cuda",
                "owner/model-a-GGUF:Q4_K_M",
                contextSize: 4096,
                temperature: 0.4),
            CreateEntry(
                "llama.cpp.cuda",
                "owner/model-b-GGUF:Q8_0",
                contextSize: 8192,
                temperature: null),
        ]);

        await store.SaveAsync(library, cancellationToken).ConfigureAwait(true);
        InferenceModelLibrary? loaded = await store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(
            4096,
            loaded.Find("llama.cpp.cuda", "owner/model-a-GGUF:Q4_K_M")?.Configuration.ContextSize);
        Assert.Equal(
            0.4,
            loaded.Find("llama.cpp.cuda", "owner/model-a-GGUF:Q4_K_M")?.Configuration.Generation.Temperature);
        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"apiKey\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"accessToken\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"secret\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"authorization\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreReplacesPreviousSnapshotAtomically()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "models.json");
        using JsonInferenceModelLibraryStore store = new(path);
        InferenceModelLibrary initial = new(
        [
            CreateEntry("provider.alpha", "owner/model-a", 4096, null),
        ]);
        InferenceModelLibrary updated = initial.WithEntry(
            CreateEntry("provider.alpha", "owner/model-b", 8192, 0.7));

        await store.SaveAsync(initial, cancellationToken).ConfigureAwait(true);
        await store.SaveAsync(updated, cancellationToken).ConfigureAwait(true);
        InferenceModelLibrary? loaded = await store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Empty(Directory.GetFiles(directory.Path, ".*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task StoreRejectsUnknownSchemaVersion()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "models.json");
        await File.WriteAllTextAsync(
            path,
            "{\"schemaVersion\":99,\"entries\":[]}",
            cancellationToken).ConfigureAwait(true);
        using JsonInferenceModelLibraryStore store = new(path);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(cancellationToken)).ConfigureAwait(true);

        Assert.Contains("schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreRejectsDuplicateStoredModelIdentity()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "models.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "entries": [
                {
                  "providerId": "provider.alpha",
                  "modelReference": "owner/model",
                  "contextSize": null,
                  "generation": {},
                  "savedAtUtc": "2026-09-19T18:00:00+00:00"
                },
                {
                  "providerId": "provider.alpha",
                  "modelReference": "owner/model",
                  "contextSize": 4096,
                  "generation": {},
                  "savedAtUtc": "2026-09-19T19:00:00+00:00"
                }
              ]
            }
            """,
            cancellationToken).ConfigureAwait(true);
        using JsonInferenceModelLibraryStore store = new(path);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(cancellationToken)).ConfigureAwait(true);

        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static InferenceModelLibraryEntry CreateEntry(
        string providerId,
        string modelReference,
        int? contextSize,
        double? temperature)
    {
        return new InferenceModelLibraryEntry(
            new InferenceProviderConfiguration(
                providerId,
                modelReference,
                contextSize,
                new InferenceGenerationOptions(temperature: temperature)),
            new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"alicia-model-library-tests-{Guid.NewGuid():N}");
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
