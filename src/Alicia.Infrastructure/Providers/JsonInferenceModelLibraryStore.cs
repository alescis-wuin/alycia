using System.Text.Json;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers;

public sealed class JsonInferenceModelLibraryStore : IInferenceModelLibraryStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _libraryPath;
    private bool _disposed;

    public JsonInferenceModelLibraryStore(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        _libraryPath = Path.GetFullPath(libraryPath);
    }

    public async Task<InferenceModelLibrary?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            LibraryDocument? document = await ReadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);
            return document is null ? null : MapToLibrary(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        InferenceModelLibrary library,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(library);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            LibraryDocument document = MapFromLibrary(library);
            await WriteDocumentAsync(document, cancellationToken).ConfigureAwait(false);
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

    private async Task<LibraryDocument?> ReadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_libraryPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(
                _libraryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            LibraryDocument? document = await JsonSerializer
                .DeserializeAsync<LibraryDocument>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                throw new InvalidDataException("Model-library document is empty.");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported model-library schema version '{document.SchemaVersion}'.");
            }

            if (document.Entries is null)
            {
                throw new InvalidDataException(
                    "Model-library document does not contain an entries collection.");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Model-library JSON is invalid.", exception);
        }
    }

    private async Task WriteDocumentAsync(
        LibraryDocument document,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_libraryPath)
            ?? throw new InvalidOperationException(
                "Model-library path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_libraryPath)}.{Guid.NewGuid():N}.tmp");

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

            File.Move(temporaryPath, _libraryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static InferenceModelLibrary MapToLibrary(LibraryDocument document)
    {
        try
        {
            return new InferenceModelLibrary(document.Entries.Select(MapToEntry));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored model-library data is invalid.",
                exception);
        }
    }

    private static InferenceModelLibraryEntry MapToEntry(ModelEntryDocument stored)
    {
        InferenceProviderConfiguration configuration = new(
            stored.ProviderId,
            stored.ModelReference,
            stored.ContextSize,
            new InferenceGenerationOptions(
                stored.Generation?.MaxOutputTokens,
                stored.Generation?.Temperature,
                stored.Generation?.TopP,
                stored.Generation?.TopK,
                stored.Generation?.Seed,
                stored.Generation?.ReasoningEnabled,
                stored.Generation?.ReasoningBudgetTokens));
        return new InferenceModelLibraryEntry(configuration, stored.SavedAtUtc);
    }

    private static LibraryDocument MapFromLibrary(InferenceModelLibrary library)
    {
        return new LibraryDocument(
            CurrentSchemaVersion,
            library.Entries.Select(entry => new ModelEntryDocument(
                entry.ProviderId,
                entry.ModelReference,
                entry.Configuration.ContextSize,
                new GenerationOptionsDocument(
                    entry.Configuration.Generation.MaxOutputTokens,
                    entry.Configuration.Generation.Temperature,
                    entry.Configuration.Generation.TopP,
                    entry.Configuration.Generation.TopK,
                    entry.Configuration.Generation.Seed,
                    entry.Configuration.Generation.ReasoningEnabled,
                    entry.Configuration.Generation.ReasoningBudgetTokens),
                entry.SavedAtUtc)).ToArray());
    }

    private sealed record LibraryDocument(
        int SchemaVersion,
        ModelEntryDocument[] Entries);

    private sealed record ModelEntryDocument(
        string ProviderId,
        string ModelReference,
        int? ContextSize,
        GenerationOptionsDocument? Generation,
        DateTimeOffset SavedAtUtc);

    private sealed record GenerationOptionsDocument(
        int? MaxOutputTokens,
        double? Temperature,
        double? TopP,
        int? TopK,
        int? Seed,
        bool? ReasoningEnabled,
        int? ReasoningBudgetTokens);
}
