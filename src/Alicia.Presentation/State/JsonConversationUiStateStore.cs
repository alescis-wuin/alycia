using System.Text.Json;

namespace Alicia.Presentation.State;

public sealed class JsonConversationUiStateStore : IConversationUiStateStore, IDisposable
{
    private const int CurrentVersion = 2;
    private const int LegacyVersion = 1;

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

                if (document is null
                    || (document.Version != CurrentVersion && document.Version != LegacyVersion))
                {
                    return ConversationUiStateSnapshot.Default;
                }

                Dictionary<string, ConversationScrollState> scrollStates = LoadScrollStates(document);
                Dictionary<string, ConversationVisualIdentity> identities = document.Version == CurrentVersion
                    ? LoadIdentities(document)
                    : new Dictionary<string, ConversationVisualIdentity>(StringComparer.Ordinal);

                return new ConversationUiStateSnapshot(
                    document.IsConversationHistoryExpanded ?? true,
                    scrollStates,
                    identities);
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
            Dictionary<string, ConversationIdentityDocument?> identities = snapshot
                .ConversationIdentities
                .ToDictionary(
                    pair => pair.Key,
                    pair => (ConversationIdentityDocument?)new ConversationIdentityDocument(
                        pair.Value.Icon.ToString(),
                        pair.Value.Color.ToString()),
                    StringComparer.Ordinal);
            UiStateDocument document = new(
                CurrentVersion,
                snapshot.IsConversationHistoryExpanded,
                conversations,
                identities);
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

    private static Dictionary<string, ConversationScrollState> LoadScrollStates(
        UiStateDocument document)
    {
        Dictionary<string, ConversationScrollState> states = new(StringComparer.Ordinal);

        foreach ((string key, ConversationScrollDocument? value) in
            document.Conversations ?? new Dictionary<string, ConversationScrollDocument?>())
        {
            if (value is null
                || !TryNormalizeConversationId(key, out string normalizedId)
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

            states[normalizedId] = new ConversationScrollState(mode, value.VerticalOffset);
        }

        return states;
    }

    private static Dictionary<string, ConversationVisualIdentity> LoadIdentities(
        UiStateDocument document)
    {
        Dictionary<string, ConversationVisualIdentity> identities = new(StringComparer.Ordinal);

        foreach ((string key, ConversationIdentityDocument? value) in
            document.Identities ?? new Dictionary<string, ConversationIdentityDocument?>())
        {
            if (value is null
                || !TryNormalizeConversationId(key, out string normalizedId)
                || !Enum.TryParse(
                    value.Icon,
                    ignoreCase: true,
                    out ConversationIdentityIcon icon)
                || !Enum.IsDefined(icon)
                || !Enum.TryParse(
                    value.Color,
                    ignoreCase: true,
                    out ConversationIdentityColor color)
                || !Enum.IsDefined(color))
            {
                continue;
            }

            identities[normalizedId] = new ConversationVisualIdentity(icon, color);
        }

        return identities;
    }

    private static bool TryNormalizeConversationId(string value, out string normalizedId)
    {
        if (Guid.TryParse(value, out Guid conversationId) && conversationId != Guid.Empty)
        {
            normalizedId = conversationId.ToString("D");
            return true;
        }

        normalizedId = string.Empty;
        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record UiStateDocument(
        int Version,
        bool? IsConversationHistoryExpanded,
        Dictionary<string, ConversationScrollDocument?>? Conversations,
        Dictionary<string, ConversationIdentityDocument?>? Identities = null);

    private sealed record ConversationScrollDocument(
        string Mode,
        double VerticalOffset);

    private sealed record ConversationIdentityDocument(
        string Icon,
        string Color);
}
