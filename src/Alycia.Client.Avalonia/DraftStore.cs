using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Alycia.Client.Avalonia;

public sealed record DraftEntry(string Text, Guid RequestId);

public sealed class DraftStore
{
    private readonly string _path;
    private Dictionary<Guid, DraftEntry> _drafts = new();
    public string? Error { get; private set; }

    public DraftStore(string? directory = null)
    {
        directory ??= Environment.GetEnvironmentVariable("ALYCIA_CLIENT_STATE_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alycia");
        _path = Path.Combine(directory, "drafts.json");
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 8 * 1024 * 1024) throw new InvalidDataException();
                _drafts = JsonSerializer.Deserialize<Dictionary<Guid, DraftEntry>>(File.ReadAllText(_path))
                    ?? throw new InvalidDataException();
                foreach (var entry in _drafts.Values)
                    if (entry is null || entry.Text is null || entry.Text.Length > 32000 || entry.RequestId == Guid.Empty)
                        throw new InvalidDataException();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { Error = "Brouillons locaux illisibles : fichier conservé, sauvegarde désactivée."; _drafts = new(); }
    }

    public DraftEntry? Get(Guid id) => _drafts.GetValueOrDefault(id);

    public DraftEntry Set(Guid id, string text)
    {
        var sameRequest = _drafts.TryGetValue(id, out var old) && old.Text.Trim() == text.Trim();
        var entry = new DraftEntry(text, sameRequest ? old!.RequestId : Guid.NewGuid());
        if (string.IsNullOrEmpty(text)) _drafts.Remove(id);
        else _drafts[id] = entry;
        return entry;
    }

    public void Remove(Guid id) => _drafts.Remove(id);

    public void Save()
    {
        if (Error is not null) return;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_drafts);
            if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.GetDirectoryName(_path)!,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, fileOptions)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, _path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Error = "Sauvegarde des brouillons indisponible ; texte conservé en mémoire."; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
