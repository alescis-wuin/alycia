using System.Collections.Immutable;
using System.Collections.Frozen;

namespace Alycia.Contracts.Indexing;

public sealed record SourceChunk(Guid FileId, string Path, string Sha256, string Symbol, string Kind,
    int Start, int End, int StartLine, int EndLine, string Text, string[] References, bool CompleteUnit, bool CompleteSource = true, string? IndexContext = null, PassageGuide? Guide = null)
{
    private readonly string[] _references = References.ToArray();
    public string[] References { get => _references.ToArray(); init => _references = value.ToArray(); }
}
public sealed record ProjectIndexSnapshot(IReadOnlyList<SourceChunk> Chunks, int EligibleFiles,
    int IndexedFiles, int ReusedFiles, IReadOnlyList<string> Omissions)
{
    public IReadOnlyList<SourceChunk> Chunks { get; } = Chunks.ToImmutableArray();
    public IReadOnlyList<string> Omissions { get; } = Omissions.ToImmutableArray();
}
public interface IProjectIndex
{
    Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct);
    async Task<ProjectIndexSnapshot> BuildScopedAsync(Guid conversation, IReadOnlySet<Guid>? allowed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var scope = allowed?.ToFrozenSet();
        var snapshot = await BuildAsync(conversation, ct);
        ct.ThrowIfCancellationRequested();
        if (scope is null) return snapshot;
        var chunks = snapshot.Chunks.Where(c => scope.Contains(c.FileId)).ToArray();
        return new(chunks, scope.Count, chunks.Select(c => c.FileId).Distinct().Count(), 0, []);
    }
}
