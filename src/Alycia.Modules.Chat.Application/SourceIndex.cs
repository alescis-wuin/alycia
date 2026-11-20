namespace Alycia.Modules.Chat.Application;

public sealed record SourceChunk(Guid FileId, string Path, string Sha256, string Symbol, string Kind,
    int Start, int End, int StartLine, int EndLine, string Text, string[] References, bool CompleteUnit, bool CompleteSource = true, string? IndexContext = null);
public sealed record ProjectIndexSnapshot(IReadOnlyList<SourceChunk> Chunks, int EligibleFiles,
    int IndexedFiles, int ReusedFiles, IReadOnlyList<string> Omissions);
public interface IProjectIndex
{
    Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct);
    async Task<ProjectIndexSnapshot> BuildScopedAsync(Guid conversation, IReadOnlySet<Guid>? allowed, CancellationToken ct)
    {
        var snapshot = await BuildAsync(conversation, ct);
        if (allowed is null) return snapshot;
        var chunks = snapshot.Chunks.Where(c => allowed.Contains(c.FileId)).ToArray();
        return new(chunks, allowed.Count, chunks.Select(c => c.FileId).Distinct().Count(), 0, []);
    }
}
