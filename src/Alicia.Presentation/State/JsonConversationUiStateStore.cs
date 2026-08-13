using System.Text.Json;

namespace Alicia.Presentation.State;

public sealed class JsonConversationUiStateStore : IConversationUiStateStore, IDisposable
{
    private const int CurrentVersion = 1;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly string _path;
    private bool _disposed;

    public JsonConversationUiStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<ConversationUiStateSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(_path))
            {
                return ConversationUiStateSnapshot.Default;
            }

            try
            {
                await using FileStream stream = new(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                UiStateDocument? document = await JsonSerializer
                    .DeserializeAsync<UiStateDocument>(stream, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (document is null || document.Version != CurrentVersion)
                {
                    return ConversationUiStateSnapshot.Default;
                }

                Dictionary<string, ConversationScrollState> states = new(StringComparer.Ordinal);

                foreach ((string key, ConversationScrollDocument? value) in
                    document.Conversations ?? new Dictionary<string, ConversationScrollDocument?>())
                {
                    if (value is null
                        || !Guid.TryParse(key, out Guid conversationId)
                        || conversationId == Guid.Empty
                        || !Enum.TryParse(
                            value.Mode,
                            ignoreCase: true,
                            out ConversationScrollMode mode)
                        || !Enum.IsDefined(mode)
                        || !double.IsFinite(value.VerticalOffset)
                        || value.VerticalOffset < 0)
                    {
                        continue;
                    }

                    states[conversationId.ToString("D")] = new ConversationScrollState(
                        mode,
                        value.VerticalOffset);
                }

                return new ConversationUiStateSnapshot(
                    document.IsConversationHistoryExpanded ?? true,
                    states);
            }
            catch (JsonException)
            {
                return ConversationUiStateSnapshot.Default;
            }
            catch (IOException)
            {
                return ConversationUiStateSnapshot.Default;
            }
            catch (UnauthorizedAccessException)
            {
                return ConversationUiStateSnapshot.Default;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ConversationUiStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        string? temporaryPath = null;

        try
        {
            string? directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Dictionary<string, ConversationScrollDocument?> conversations = snapshot
                .ConversationScrollStates
                .ToDictionary(
                    pair => pair.Key,
                    pair => (ConversationScrollDocument?)new ConversationScrollDocument(
                        pair.Value.Mode.ToString(),
                        pair.Value.VerticalOffset),
                    StringComparer.Ordinal);
            UiStateDocument document = new(
                CurrentVersion,
                snapshot.IsConversationHistoryExpanded,
                conversations);
            temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";

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

            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

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

    private sealed record UiStateDocument(
        int Version,
        bool? IsConversationHistoryExpanded,
        Dictionary<string, ConversationScrollDocument?>? Conversations);

    private sealed record ConversationScrollDocument(
        string Mode,
        double VerticalOffset);
}
