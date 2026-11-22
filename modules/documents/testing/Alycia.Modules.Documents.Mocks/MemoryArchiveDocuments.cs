using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed record ArchiveFile(Guid Owner, Guid? Workspace, string Name, string Hash, int Sequence);

public sealed class MemoryArchiveDocuments(IEnumerable<ArchiveFile> files, IEnumerable<(Guid Owner, Guid File)> overrides) : IArchiveDocuments
{
    private readonly List<ArchiveFile> _files = files.ToList();
    private readonly HashSet<(Guid Owner, Guid File)> _overrides = overrides.ToHashSet();
    private readonly Dictionary<Guid, string[]> _originals = [];
    public string[] Originals(Guid owner) => _originals.GetValueOrDefault(owner)?.ToArray() ?? [];
    public int Attached(Guid owner) => _files.Count(f => f.Owner == owner);
    public int Overrides(Guid owner) => _overrides.Count(x => x.Owner == owner);
    public Task<string[]> DescribeWorkspaceAsync(Guid workspace, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.Where(f => f.Workspace == workspace).OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => f.Name + " · " + f.Hash).ToArray());
    }
    public IDocumentFork Fork(Guid source, Guid destination, int through, bool copyOverrides) =>
        new Attempt(this, source, destination, through, copyOverrides);

    private sealed class Attempt(MemoryArchiveDocuments storage, Guid source, Guid target, int through, bool copyOverrides) : IDocumentFork
    {
        private readonly List<ArchiveFile> _copied = [];
        private readonly List<(Guid Owner, Guid File)> _modes = [];
        private bool _created;
        public Task PrepareAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var files = storage._files.Where(f => f.Owner == source && f.Sequence <= through + 1).ToArray();
            if (files.Length > 0)
            {
                if (!storage._originals.TryAdd(target, files.Select(f => f.Hash).ToArray())) throw new IOException("Destination occupée.");
                _created = true;
            }
            _copied.AddRange(files.Select(f => f with { Owner = target, Workspace = null }));
            storage._files.AddRange(_copied);
            if (copyOverrides) _modes.AddRange(storage._overrides.Where(x => x.Owner == source).Select(x => (target, x.File)));
            storage._overrides.UnionWith(_modes);
            return Task.CompletedTask;
        }
        public void Detach(bool discardOriginals)
        {
            foreach (var copy in _copied) storage._files.Remove(copy);
            foreach (var mode in _modes) storage._overrides.Remove(mode);
            if (discardOriginals && _created) storage._originals.Remove(target);
        }
    }
}
