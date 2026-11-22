using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed record OriginalFixture(Guid Reader, Guid File, string Name, DateTimeOffset Modified,
    byte[]? Bytes, bool Allowed = true);

public sealed class SnapshotOriginals(IEnumerable<OriginalFixture> originals) : IConversationOriginals
{
    private readonly Dictionary<(Guid, Guid), OriginalFixture> _entries = originals
        .ToDictionary(f => (f.Reader, f.File), f => f with { Bytes = f.Bytes?.ToArray() });

    public Task<DownloadedOriginal?> DownloadAsync(Guid conversation, Guid file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue((conversation, file), out var found) || !found.Allowed) return Task.FromResult<DownloadedOriginal?>(null);
        if (found.Bytes is null) throw new OriginalUnavailableException(new FileNotFoundException());
        return Task.FromResult<DownloadedOriginal?>(new(new MemoryStream(found.Bytes.ToArray(), writable: false), Path.GetFileName(found.Name), found.Modified));
    }
}
