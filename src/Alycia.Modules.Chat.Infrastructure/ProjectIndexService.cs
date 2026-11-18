using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectIndexService(ChatDbContext database, FileStorage storage, ProjectSourceReader reader) : IProjectIndex
{
    public const int MaxCharacters = 8_000_000;
    private sealed record Cached(int Version, string Sha256, string Path, string Status, SourceChunk[] Chunks, int ExtractorVersion = 0);
    public async Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct)
    {
        if (!await database.Conversations.AnyAsync(c => c.Id == conversation, ct)) throw new ConversationNotFoundException();
        var files = await database.ModelSources(conversation).Where(f => f.ContextMode != "excluded" && f.Text != "").OrderBy(f => f.Name).ThenBy(f => f.Id).ToArrayAsync(ct);
        files = files.OrderByDescending(f => f.ContextMode == "pinned").DistinctBy(f => (f.Name, f.Sha256)).ToArray();
        var chunks = new List<SourceChunk>(); var omissions = new List<string>(); var characters = 0; var indexed = 0; var reused = 0;
        var directory = Path.Combine(storage.DirectoryFor(conversation), "index-v1");
        Directory.CreateDirectory(directory);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var cachePath = Path.Combine(directory, file.Id.ToString("N") + ".json");
            try
            {
                Cached? cache = null;
                if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= 64 * 1024 * 1024)
                {
                    try { cache = JsonSerializer.Deserialize<Cached>(await File.ReadAllTextAsync(cachePath, ct), ProjectToolCatalog.Json); }
                    catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
                }
                var valid = cache is not null && cache.Version == CodeStructure.Version && cache.ExtractorVersion == StructuredDocuments.Version && cache.Sha256 == file.Sha256
                    && cache.Path == file.Name && cache.Status == file.Status
                    && cache.Chunks is not null && cache.Chunks.All(c => c is not null && c.FileId == file.Id
                        && c.Sha256 == file.Sha256 && c.Path == file.Name && c.Text is not null && c.Symbol is not null
                        && c.References is not null && c.Start >= 0 && c.End - c.Start == c.Text.Length);
                SourceChunk[] parts;
                if (valid) { parts = cache!.Chunks!; }
                else
                {
                    var original = await reader.ReadAsync(file, ct);
                    if (characters + original.Text.Length > MaxCharacters)
                    { omissions.Add(file.Name + " : budget d’index atteint ; original accessible par outils"); continue; }
                    parts = CodeStructure.Split(file.Id, file.Name, file.Sha256, original.Text, ct).Select(c => c with { CompleteSource = original.Complete }).ToArray();
                    var temp = cachePath + "." + Guid.NewGuid().ToString("N");
                    try
                    {
                        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new Cached(CodeStructure.Version, file.Sha256, file.Name, file.Status, parts, StructuredDocuments.Version), ProjectToolCatalog.Json), ct);
                        ct.ThrowIfCancellationRequested(); File.Move(temp, cachePath, true);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                var size = parts.Sum(p => p.Text.Length);
                if (characters + size > MaxCharacters)
                { omissions.Add(file.Name + " : budget d’index atteint ; original accessible par outils"); continue; }
                if (parts.Any(c => !c.CompleteSource)) omissions.Add(file.Name + " : extraction documentaire partielle");
                characters += size; chunks.AddRange(parts); indexed++; if (valid) reused++;
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
            { omissions.Add(file.Name + " : source ou cache indisponible"); }
        }
        // Removed/excluded sources cannot remain visible through cache lookup or subsequent retrieval.
        var active = files.Select(f => f.Id.ToString("N") + ".json").ToHashSet(StringComparer.Ordinal);
        foreach (var stale in Directory.EnumerateFiles(directory, "*.json"))
            if (!active.Contains(Path.GetFileName(stale))) File.Delete(stale);
        return new(chunks, files.Length, indexed, reused, omissions);
    }
}
