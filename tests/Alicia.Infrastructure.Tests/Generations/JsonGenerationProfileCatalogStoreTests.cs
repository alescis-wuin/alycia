using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Infrastructure.Generations;

namespace Alicia.Infrastructure.Tests.Generations;

public sealed class JsonGenerationProfileCatalogStoreTests
{
    [Fact]
    public async Task MissingCatalogFileReturnsNoCatalog()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        using JsonGenerationProfileCatalogStore store = new(
            Path.Combine(directory.Path, "profiles", "catalogs.json"));
        GenerationProfileModelScope scope = new(
            "provider.alpha",
            "owner/model-GGUF:Q4_K_M");

        GenerationProfileCatalog? loaded = await store
            .LoadAsync(scope, cancellationToken)
            .ConfigureAwait(true);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task StoreRoundTripsConfirmedRevisionsAndPersistentDrafts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles", "catalogs.json");
        using JsonGenerationProfileCatalogStore store = new(path);
        GenerationProfileModelScope scope = new(
            "provider.alpha",
            "owner/model-GGUF:Q4_K_M");
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            GenerationProfileId.New());
        GenerationProfileId codeProfileId = GenerationProfileId.New();
        GenerationProfileWorkingDraft initialDraft = new(
            new GenerationProfile(
                codeProfileId,
                "Code",
                "Prefer testable implementation details.",
                new InferenceGenerationOptions(
                    maxOutputTokens: 768,
                    temperature: 0.25,
                    topP: 0.92,
                    topK: 32,
                    seed: 17,
                    reasoningEnabled: true,
                    reasoningBudgetTokens: 256),
                new List<string> { "Explain this code", "Review this change" }),
            baseRevisionId: null,
            new DateTimeOffset(2026, 9, 16, 18, 0, 0, TimeSpan.Zero));
        GenerationProfileRevisionId confirmedRevisionId = GenerationProfileRevisionId.New();
        catalog = catalog
            .WithWorkingDraft(initialDraft)
            .CommitWorkingDraft(
                codeProfileId,
                confirmedRevisionId,
                new DateTimeOffset(2026, 9, 16, 18, 1, 0, TimeSpan.Zero));
        GenerationProfileWorkingDraft pendingDraft = new(
            new GenerationProfile(
                codeProfileId,
                "Code",
                "Prefer testable implementation details.",
                new InferenceGenerationOptions(
                    maxOutputTokens: 768,
                    temperature: 0.35,
                    topP: 0.92,
                    topK: 32,
                    seed: 17,
                    reasoningEnabled: true,
                    reasoningBudgetTokens: 256),
                new List<string> { "Explain this code", "Review this change" }),
            confirmedRevisionId,
            new DateTimeOffset(2026, 9, 16, 18, 2, 0, TimeSpan.Zero));
        catalog = catalog.WithWorkingDraft(pendingDraft);

        await store.SaveAsync(catalog, cancellationToken).ConfigureAwait(true);
        GenerationProfileCatalog? loaded = await store
            .LoadAsync(scope, cancellationToken)
            .ConfigureAwait(true);

        Assert.NotNull(loaded);
        Assert.Equal(scope, loaded.Scope);
        Assert.Equal(catalog.DefaultProfile.Id, loaded.DefaultProfile.Id);
        GenerationProfileRevision revision = Assert.Single(loaded.Revisions);
        Assert.Equal(confirmedRevisionId, revision.Id);
        Assert.Equal(catalog.Revisions[0].PayloadHash, revision.PayloadHash);
        GenerationProfileWorkingDraft draft = Assert.Single(loaded.WorkingDrafts);
        Assert.Equal(confirmedRevisionId, draft.BaseRevisionId);
        Assert.Equal(0.35, draft.Profile.GenerationOptions.Temperature);
        Assert.Equal(2, loaded.Profiles.Count);
        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains(revision.PayloadHash, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingOneModelScopePreservesOtherModelCatalogs()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        using JsonGenerationProfileCatalogStore store = new(
            Path.Combine(directory.Path, "profiles.json"));
        GenerationProfileModelScope firstScope = new("provider.alpha", "owner/model-a");
        GenerationProfileModelScope secondScope = new("provider.alpha", "owner/model-b");
        GenerationProfileCatalog first = GenerationProfileCatalog.CreateEmpty(
            firstScope,
            GenerationProfileId.New());
        GenerationProfileCatalog second = GenerationProfileCatalog.CreateEmpty(
            secondScope,
            GenerationProfileId.New());

        await store.SaveAsync(first, cancellationToken).ConfigureAwait(true);
        await store.SaveAsync(second, cancellationToken).ConfigureAwait(true);

        GenerationProfileCatalog? loadedFirst = await store
            .LoadAsync(firstScope, cancellationToken)
            .ConfigureAwait(true);
        GenerationProfileCatalog? loadedSecond = await store
            .LoadAsync(secondScope, cancellationToken)
            .ConfigureAwait(true);

        Assert.NotNull(loadedFirst);
        Assert.NotNull(loadedSecond);
        Assert.Equal(first.DefaultProfile.Id, loadedFirst.DefaultProfile.Id);
        Assert.Equal(second.DefaultProfile.Id, loadedSecond.DefaultProfile.Id);
    }

    [Fact]
    public async Task StoreRejectsUnsupportedSchemaAndDuplicateScopes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        using JsonGenerationProfileCatalogStore store = new(path);
        await File.WriteAllTextAsync(
            path,
            "{\"schemaVersion\":99,\"catalogs\":[]}",
            cancellationToken).ConfigureAwait(true);

        InvalidDataException schemaException = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(
                new GenerationProfileModelScope("provider.alpha", "owner/model"),
                cancellationToken)).ConfigureAwait(true);
        Assert.Contains(
            "schema version",
            schemaException.Message,
            StringComparison.OrdinalIgnoreCase);

        string duplicateDocument = """
            {
              "schemaVersion": 1,
              "catalogs": [
                {
                  "providerId": "provider.alpha",
                  "modelReference": "owner/model",
                  "defaultProfileId": "11111111-1111-1111-1111-111111111111",
                  "revisions": [],
                  "workingDrafts": []
                },
                {
                  "providerId": " provider.alpha ",
                  "modelReference": " owner/model ",
                  "defaultProfileId": "22222222-2222-2222-2222-222222222222",
                  "revisions": [],
                  "workingDrafts": []
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(
            path,
            duplicateDocument,
            cancellationToken).ConfigureAwait(true);

        InvalidDataException duplicateException = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(
                new GenerationProfileModelScope("provider.alpha", "owner/model"),
                cancellationToken)).ConfigureAwait(true);
        Assert.Contains(
            "duplicate scope",
            duplicateException.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreRejectsTamperedRevisionPayloadHash()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        using JsonGenerationProfileCatalogStore store = new(path);
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            GenerationProfileId.New());
        GenerationProfileId profileId = GenerationProfileId.New();
        catalog = catalog
            .WithWorkingDraft(new GenerationProfileWorkingDraft(
                new GenerationProfile(profileId, "Code"),
                baseRevisionId: null,
                DateTimeOffset.UtcNow))
            .CommitWorkingDraft(
                profileId,
                GenerationProfileRevisionId.New(),
                DateTimeOffset.UtcNow.AddMinutes(1));

        await store.SaveAsync(catalog, cancellationToken).ConfigureAwait(true);
        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
        string originalHash = catalog.Revisions[0].PayloadHash;
        string tampered = json.Replace(
            originalHash,
            new string('0', originalHash.Length),
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, tampered, cancellationToken).ConfigureAwait(true);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(scope, cancellationToken)).ConfigureAwait(true);

        Assert.Contains("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AtomicSaveCreatesParentDirectoryAndLeavesNoTemporaryFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string catalogDirectory = Path.Combine(directory.Path, "nested", "profiles");
        string path = Path.Combine(catalogDirectory, "catalogs.json");
        using JsonGenerationProfileCatalogStore store = new(path);
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            new GenerationProfileModelScope("provider.alpha", "owner/model"),
            GenerationProfileId.New());

        await store.SaveAsync(catalog, cancellationToken).ConfigureAwait(true);

        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(catalogDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"alicia-generation-profile-catalog-tests-{Guid.NewGuid():N}");
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
