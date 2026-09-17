using System.Text.Json;
using Alicia.Application.Generations;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Generations;

public sealed class JsonConversationGenerationSelectionStore :
    IConversationGenerationSelectionStore,
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
    private readonly string _selectionPath;
    private bool _disposed;

    public JsonConversationGenerationSelectionStore(string selectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionPath);
        _selectionPath = Path.GetFullPath(selectionPath);
    }

    public async Task<ConversationGenerationSelection?> LoadAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateConversationId(conversationId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IReadOnlyList<ConversationGenerationSelection> selections =
                await ReadSelectionsAsync(cancellationToken).ConfigureAwait(false);

            foreach (ConversationGenerationSelection selection in selections)
            {
                if (selection.ConversationId == conversationId)
                {
                    return selection;
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
        ConversationGenerationSelection selection,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(selection);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IReadOnlyList<ConversationGenerationSelection> existing =
                await ReadSelectionsAsync(cancellationToken).ConfigureAwait(false);
            List<ConversationGenerationSelection> updated = new(existing.Count + 1);

            foreach (ConversationGenerationSelection candidate in existing)
            {
                if (candidate.ConversationId != selection.ConversationId)
                {
                    updated.Add(candidate);
                }
            }

            updated.Add(selection);
            updated.Sort(static (left, right) =>
                left.ConversationId.Value.CompareTo(right.ConversationId.Value));
            await WriteSelectionsAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateConversationId(conversationId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IReadOnlyList<ConversationGenerationSelection> existing =
                await ReadSelectionsAsync(cancellationToken).ConfigureAwait(false);
            List<ConversationGenerationSelection> updated = new(existing.Count);
            bool removed = false;

            foreach (ConversationGenerationSelection selection in existing)
            {
                if (selection.ConversationId == conversationId)
                {
                    removed = true;
                }
                else
                {
                    updated.Add(selection);
                }
            }

            if (!removed)
            {
                return false;
            }

            await WriteSelectionsAsync(updated, cancellationToken).ConfigureAwait(false);
            return true;
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

    private async Task<IReadOnlyList<ConversationGenerationSelection>> ReadSelectionsAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_selectionPath))
        {
            return Array.Empty<ConversationGenerationSelection>();
        }

        try
        {
            await using FileStream stream = new(
                _selectionPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            SelectionStoreDocument? document = await JsonSerializer
                .DeserializeAsync<SelectionStoreDocument>(
                    stream,
                    _jsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                throw new InvalidDataException(
                    "Conversation generation selection document is empty.");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported conversation generation selection schema version '{document.SchemaVersion}'.");
            }

            if (document.Selections is null)
            {
                throw new InvalidDataException(
                    "Conversation generation selection document does not contain a selection list.");
            }

            List<ConversationGenerationSelection> selections = new(document.Selections.Count);
            HashSet<ConversationId> conversationIds = new();

            foreach (SelectionDocument stored in document.Selections)
            {
                ConversationGenerationSelection selection = MapToSelection(stored);
                if (!conversationIds.Add(selection.ConversationId))
                {
                    throw new InvalidDataException(
                        $"Conversation generation selection '{selection.ConversationId}' is stored more than once.");
                }

                selections.Add(selection);
            }

            return selections;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Conversation generation selection JSON is invalid.",
                exception);
        }
    }

    private async Task WriteSelectionsAsync(
        IReadOnlyList<ConversationGenerationSelection> selections,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_selectionPath)
            ?? throw new InvalidOperationException(
                "Conversation generation selection path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_selectionPath)}.{Guid.NewGuid():N}.tmp");
        SelectionStoreDocument document = new(
            CurrentSchemaVersion,
            selections.Select(MapFromSelection).ToList());

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

            File.Move(temporaryPath, _selectionPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ConversationGenerationSelection MapToSelection(SelectionDocument stored)
    {
        try
        {
            return new ConversationGenerationSelection(
                new ConversationId(stored.ConversationId),
                new GenerationProfileModelScope(
                    stored.ProviderId,
                    stored.ModelReference),
                new GenerationProfileId(stored.ProfileId));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored conversation generation selection is invalid.",
                exception);
        }
    }

    private static SelectionDocument MapFromSelection(
        ConversationGenerationSelection selection)
    {
        return new SelectionDocument(
            selection.ConversationId.Value,
            selection.ModelScope.ProviderId,
            selection.ModelScope.ModelReference,
            selection.ProfileId.Value);
    }

    private static void ValidateConversationId(ConversationId conversationId)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }
    }

    private sealed record SelectionStoreDocument(
        int SchemaVersion,
        List<SelectionDocument>? Selections);

    private sealed record SelectionDocument(
        Guid ConversationId,
        string ProviderId,
        string ModelReference,
        Guid ProfileId);
}
