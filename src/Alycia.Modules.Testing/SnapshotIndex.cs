namespace Alycia.Modules.Testing;

public sealed class SnapshotIndex(Guid owner, IReadOnlyList<SourceChunk> passages, params Guid[] emptyOwners) : IProjectIndex
{
    private readonly HashSet<Guid> _owners = emptyOwners.Append(owner).ToHashSet();
    private readonly SourceChunk[] _passages = passages.Select(Copy).ToArray();

    public Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct) =>
        BuildScopedAsync(conversation, null, ct);

    public Task<ProjectIndexSnapshot> BuildScopedAsync(Guid conversation, IReadOnlySet<Guid>? allowed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_owners.Contains(conversation)) throw new ConversationNotFoundException();
        var selected = conversation == owner
            ? _passages.Where(p => allowed is null || allowed.Contains(p.FileId)).Select(Copy).ToArray()
            : [];
        var count = selected.Select(p => p.FileId).Distinct().Count();
        return Task.FromResult(new ProjectIndexSnapshot(selected, count, count, 0, []));
    }

    private static SourceChunk Copy(SourceChunk chunk) => chunk with { References = chunk.References.ToArray() };
}
