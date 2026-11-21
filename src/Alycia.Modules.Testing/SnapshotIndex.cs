namespace Alycia.Modules.Testing;

public sealed class SnapshotIndex(Guid owner, IReadOnlyList<SourceChunk> passages) : IProjectIndex
{
    private readonly SourceChunk[] _passages = passages.Select(p => p with { References = p.References.ToArray() }).ToArray();

    public Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = conversation == owner ? _passages.Select(p => p with { References = p.References.ToArray() }).ToArray() : [];
        var count = selected.Select(passage => passage.FileId).Distinct().Count();
        return Task.FromResult(new ProjectIndexSnapshot(selected, count, count, 0, []));
    }
}
