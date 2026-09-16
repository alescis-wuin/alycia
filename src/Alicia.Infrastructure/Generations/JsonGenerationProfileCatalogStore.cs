using System.Text.Json;
using Alicia.Application.Generations;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Generations;

public sealed class JsonGenerationProfileCatalogStore :
    IGenerationProfileCatalogStore,
    IDisposable
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _catalogPath;
    private bool _disposed;

    public JsonGenerationProfileCatalogStore(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        _catalogPath = Path.GetFullPath(catalogPath);
    }

    public async Task<GenerationProfileCatalog?> LoadAsync(
        GenerationProfileModelScope scope,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (scope.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile model scope cannot be empty.",
                nameof(scope));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            CatalogStoreDocument? document = await ReadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                return null;
            }

            foreach (CatalogDocument entry in document.Catalogs!)
            {
                if (MapScope(entry) == scope)
                {
                    return MapToCatalog(entry);
                }
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        GenerationProfileCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(catalog);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            CatalogStoreDocument document = await ReadDocumentAsync(cancellationToken)
                    .ConfigureAwait(false)
                ?? new CatalogStoreDocument(
                    CurrentSchemaVersion,
                    new List<CatalogDocument>());
            List<CatalogDocument> catalogs = new(document.Catalogs!);
            CatalogDocument replacement = MapFromCatalog(catalog);
            int replacementIndex = -1;

            for (int index = 0; index < catalogs.Count; index++)
            {
                if (MapScope(catalogs[index]) == catalog.Scope)
                {
                    replacementIndex = index;
                    break;
                }
            }

            if (replacementIndex >= 0)
            {
                catalogs[replacementIndex] = replacement;
            }
            else
            {
                catalogs.Add(replacement);
            }

            await WriteDocumentAsync(
                new CatalogStoreDocument(CurrentSchemaVersion, catalogs),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private async Task<CatalogStoreDocument?> ReadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_catalogPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(
                _catalogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            CatalogStoreDocument? document = await JsonSerializer
                .DeserializeAsync<CatalogStoreDocument>(
                    stream,
                    _jsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                throw new InvalidDataException(
                    "Generation-profile catalog document is empty.");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported generation-profile catalog schema version '{document.SchemaVersion}'.");
            }

            if (document.Catalogs is null)
            {
                throw new InvalidDataException(
                    "Generation-profile catalog document does not contain a catalog list.");
            }

            ValidateCatalogScopes(document.Catalogs);
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Generation-profile catalog JSON is invalid.",
                exception);
        }
    }

    private async Task WriteDocumentAsync(
        CatalogStoreDocument document,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_catalogPath)
            ?? throw new InvalidOperationException(
                "Generation-profile catalog path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_catalogPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer
                    .SerializeAsync(stream, document, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _catalogPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateCatalogScopes(IReadOnlyList<CatalogDocument> catalogs)
    {
        HashSet<GenerationProfileModelScope> scopes = new();

        foreach (CatalogDocument? catalog in catalogs)
        {
            if (catalog is null)
            {
                throw new InvalidDataException(
                    "Generation-profile catalog document contains a null catalog entry.");
            }

            GenerationProfileModelScope scope = MapScope(catalog);
            if (!scopes.Add(scope))
            {
                throw new InvalidDataException(
                    $"Generation-profile catalog document contains duplicate scope '{scope}'.");
            }
        }
    }

    private static GenerationProfileModelScope MapScope(CatalogDocument document)
    {
        try
        {
            return new GenerationProfileModelScope(
                document.ProviderId ?? string.Empty,
                document.ModelReference ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored generation-profile model scope is invalid.",
                exception);
        }
    }

    private static GenerationProfileCatalog MapToCatalog(CatalogDocument document)
    {
        List<RevisionDocument>? storedRevisions = document.Revisions;
        List<WorkingDraftDocument>? storedDrafts = document.WorkingDrafts;
        if (storedRevisions is null || storedDrafts is null)
        {
            throw new InvalidDataException(
                "Stored generation-profile catalog is missing revision or draft collections.");
        }

        try
        {
            GenerationProfileModelScope scope = MapScope(document);
            GenerationProfile defaultProfile = GenerationProfile.CreateDefault(
                new GenerationProfileId(document.DefaultProfileId));
            List<GenerationProfileRevision> revisions = new(storedRevisions.Count);
            foreach (RevisionDocument? revisionDocument in storedRevisions)
            {
                if (revisionDocument is null)
                {
                    throw new InvalidDataException(
                        "Stored generation-profile revision entry is null.");
                }

                revisions.Add(MapToRevision(revisionDocument));
            }

            List<GenerationProfileWorkingDraft> drafts = new(storedDrafts.Count);
            foreach (WorkingDraftDocument? draftDocument in storedDrafts)
            {
                if (draftDocument is null)
                {
                    throw new InvalidDataException(
                        "Stored generation-profile working-draft entry is null.");
                }

                drafts.Add(MapToWorkingDraft(draftDocument));
            }

            return new GenerationProfileCatalog(
                scope,
                defaultProfile,
                revisions,
                drafts);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored generation-profile catalog is invalid.",
                exception);
        }
    }

    private static GenerationProfileRevision MapToRevision(RevisionDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.PayloadHash))
        {
            throw new InvalidDataException(
                "Stored generation-profile revision does not contain a payload hash.");
        }

        GenerationProfile profile = MapToProfile(document.Profile);
        GenerationProfileRevision revision;

        try
        {
            revision = new GenerationProfileRevision(
                new GenerationProfileRevisionId(document.Id),
                document.ParentRevisionId is Guid parentRevisionId
                    ? new GenerationProfileRevisionId(parentRevisionId)
                    : null,
                document.CreatedAtUtc,
                profile);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored generation-profile revision is invalid.",
                exception);
        }

        if (!string.Equals(
            revision.PayloadHash,
            document.PayloadHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Stored generation-profile revision '{revision.Id}' has a payload-hash mismatch.");
        }

        return revision;
    }

    private static GenerationProfileWorkingDraft MapToWorkingDraft(
        WorkingDraftDocument document)
    {
        GenerationProfile profile = MapToProfile(document.Profile);

        try
        {
            return new GenerationProfileWorkingDraft(
                profile,
                document.BaseRevisionId is Guid baseRevisionId
                    ? new GenerationProfileRevisionId(baseRevisionId)
                    : null,
                document.UpdatedAtUtc);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored generation-profile working draft is invalid.",
                exception);
        }
    }

    private static GenerationProfile MapToProfile(ProfileDocument? document)
    {
        if (document is null)
        {
            throw new InvalidDataException(
                "Stored generation-profile payload is incomplete.");
        }

        GenerationOptionsDocument? storedGeneration = document.GenerationOptions;
        List<string>? storedSuggestions = document.InitialSuggestions;
        if (string.IsNullOrWhiteSpace(document.Name)
            || storedGeneration is null
            || storedSuggestions is null)
        {
            throw new InvalidDataException(
                "Stored generation-profile payload is incomplete.");
        }

        List<string> suggestions = new(storedSuggestions.Count);
        foreach (string? suggestion in storedSuggestions)
        {
            if (suggestion is null)
            {
                throw new InvalidDataException(
                    "Stored generation-profile suggestions contain a null value.");
            }

            suggestions.Add(suggestion);
        }

        try
        {
            GenerationOptionsDocument generation = storedGeneration;
            return new GenerationProfile(
                new GenerationProfileId(document.Id),
                document.Name,
                document.BaseSystemInstructions,
                new InferenceGenerationOptions(
                    generation.MaxOutputTokens,
                    generation.Temperature,
                    generation.TopP,
                    generation.TopK,
                    generation.Seed,
                    generation.ReasoningEnabled,
                    generation.ReasoningBudgetTokens),
                suggestions);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored generation-profile payload is invalid.",
                exception);
        }
    }

    private static CatalogDocument MapFromCatalog(GenerationProfileCatalog catalog)
    {
        List<RevisionDocument> revisions = new(catalog.Revisions.Count);
        foreach (GenerationProfileRevision revision in catalog.Revisions)
        {
            revisions.Add(new RevisionDocument(
                revision.Id.Value,
                revision.ParentRevisionId?.Value,
                revision.CreatedAtUtc,
                revision.PayloadHash,
                MapFromProfile(revision.Profile)));
        }

        List<WorkingDraftDocument> drafts = new(catalog.WorkingDrafts.Count);
        foreach (GenerationProfileWorkingDraft draft in catalog.WorkingDrafts)
        {
            drafts.Add(new WorkingDraftDocument(
                draft.BaseRevisionId?.Value,
                draft.UpdatedAtUtc,
                MapFromProfile(draft.Profile)));
        }

        return new CatalogDocument(
            catalog.Scope.ProviderId,
            catalog.Scope.ModelReference,
            catalog.DefaultProfile.Id.Value,
            revisions,
            drafts);
    }

    private static ProfileDocument MapFromProfile(GenerationProfile profile)
    {
        InferenceGenerationOptions generation = profile.GenerationOptions;
        List<string> suggestions = new(profile.InitialSuggestions.Count);
        foreach (string suggestion in profile.InitialSuggestions)
        {
            suggestions.Add(suggestion);
        }

        return new ProfileDocument(
            profile.Id.Value,
            profile.Name,
            profile.BaseSystemInstructions,
            new GenerationOptionsDocument(
                generation.MaxOutputTokens,
                generation.Temperature,
                generation.TopP,
                generation.TopK,
                generation.Seed,
                generation.ReasoningEnabled,
                generation.ReasoningBudgetTokens),
            suggestions);
    }

    private sealed record CatalogStoreDocument(
        int SchemaVersion,
        List<CatalogDocument>? Catalogs);

    private sealed record CatalogDocument(
        string? ProviderId,
        string? ModelReference,
        Guid DefaultProfileId,
        List<RevisionDocument>? Revisions,
        List<WorkingDraftDocument>? WorkingDrafts);

    private sealed record RevisionDocument(
        Guid Id,
        Guid? ParentRevisionId,
        DateTimeOffset CreatedAtUtc,
        string? PayloadHash,
        ProfileDocument? Profile);

    private sealed record WorkingDraftDocument(
        Guid? BaseRevisionId,
        DateTimeOffset UpdatedAtUtc,
        ProfileDocument? Profile);

    private sealed record ProfileDocument(
        Guid Id,
        string? Name,
        string? BaseSystemInstructions,
        GenerationOptionsDocument? GenerationOptions,
        List<string>? InitialSuggestions);

    private sealed record GenerationOptionsDocument(
        int? MaxOutputTokens,
        double? Temperature,
        double? TopP,
        int? TopK,
        int? Seed,
        bool? ReasoningEnabled,
        int? ReasoningBudgetTokens);
}
