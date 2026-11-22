using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed record ToolFixture(Guid Owner, bool Project, ToolSourceEntry Entry, AuthorizedToolSource Original, SourceChunk[] Declarations);

public sealed class SnapshotToolSources(IEnumerable<ToolFixture> files) : IAuthorizedToolSources, ILiveIndexFileAccess
{
    private readonly ToolFixture[] _files = files.Select(f => f with { Declarations = f.Declarations.Select(Copy).ToArray() }).ToArray();
    private static SourceChunk Copy(SourceChunk chunk) => chunk with { References = chunk.References.ToArray() };

    public Task<bool> HasFilesAsync(Guid conversation, CancellationToken ct) => IsReadableAsync(conversation, false, null, ct);

    private Task<bool> IsReadableAsync(Guid owner, bool project, Guid? id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.Any(f => f.Owner == owner && f.Project == project && f.Entry.Readable &&
            (!id.HasValue || f.Entry.Id == id)));
    }

    public Task<bool> IsAllowedAsync(Guid owner, bool project, Guid file, CancellationToken ct) => IsReadableAsync(owner, project, file, ct);

    public Task<ToolSourceEntry[]> EntriesAsync(Guid conversation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.Where(f => !f.Project && f.Owner == conversation).Select(f => f.Entry)
            .OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Id).ToArray());
    }

    public async Task<ToolSourceEntry[]> CurrentEntriesAsync(Guid conversation, IReadOnlyCollection<Guid> candidates, CancellationToken ct)
    {
        var ids = candidates.ToHashSet();
        return (await EntriesAsync(conversation, ct)).Where(f => ids.Contains(f.Id)).ToArray();
    }

    public Task<AuthorizedToolSource> ReadAsync(Guid conversation, Guid file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var found = _files.SingleOrDefault(f => !f.Project && f.Owner == conversation && f.Entry.Id == file && f.Entry.Readable);
        return Task.FromResult(found?.Original ?? throw new ArgumentException("Fichier introuvable dans cette conversation."));
    }

    public IReadOnlyList<SourceChunk> Declarations(AuthorizedToolSource file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _files.FirstOrDefault(f => f.Original == file)?.Declarations.Select(Copy).ToArray() ?? [];
    }
}
