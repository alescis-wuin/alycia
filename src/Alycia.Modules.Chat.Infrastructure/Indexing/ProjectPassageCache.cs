using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Infrastructure;

internal sealed class ProjectPassageCache
{
    private sealed record Cached(int Version, string Sha256, string Path, string Status, SourceChunk[] Chunks, int ExtractorVersion = 0);
    private readonly string _directory;

    public ProjectPassageCache(FileStorage storage, Guid conversation, bool hierarchical)
    {
        _directory = Path.Combine(storage.DirectoryFor(conversation), hierarchical ? "index-hierarchy-v1" : "index-v1");
        Directory.CreateDirectory(_directory);
    }

    private string PathFor(Guid file) => Path.Combine(_directory, file.ToString("N") + ".json");

    public async Task<SourceChunk[]?> TryReadAsync(IndexSourceFile file, CancellationToken ct)
    {
        var path = PathFor(file.Id);
        if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024 * 1024) return null;
        try
        {
            var cached = JsonSerializer.Deserialize<Cached>(await File.ReadAllTextAsync(path, ct), ProjectToolCatalog.Json);
            if (cached is null || cached.Version != CodeStructure.Version || cached.ExtractorVersion != StructuredDocuments.Version
                || cached.Sha256 != file.Sha256 || cached.Path != file.Name || cached.Status != file.Status) return null;
            return cached.Chunks is not null && cached.Chunks.All(c => c is not null && c.FileId == file.Id
                && c.Sha256 == file.Sha256 && c.Path == file.Name && c.Text is not null && c.Symbol is not null
                && c.References is not null && c.Start >= 0 && c.End - c.Start == c.Text.Length) ? cached.Chunks : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task WriteAsync(IndexSourceFile file, SourceChunk[] chunks, CancellationToken ct)
    {
        var path = PathFor(file.Id);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var entry = new Cached(CodeStructure.Version, file.Sha256, file.Name, file.Status, chunks, StructuredDocuments.Version);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entry, ProjectToolCatalog.Json), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void RemoveExcept(IEnumerable<Guid> ids)
    {
        var active = ids.Select(id => id.ToString("N") + ".json").ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
            if (!active.Contains(Path.GetFileName(path))) File.Delete(path);
    }
}
