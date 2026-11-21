using Alycia.Contracts.Indexing;

namespace Alycia.Modules.Indexing;

public static class IndexFiles
{
    public static async Task<IndexSourceFile[]> SnapshotAsync(IIndexSourceDocuments source, Guid owner, bool project,
        Guid[]? allowed, bool orderByName, CancellationToken ct)
    {
        var scope = allowed?.ToArray();
        const int size = 128;
        List<IndexSourceFile> result = [];
        IndexFileCursor? after = null;
        while (true)
        {
            var page = await source.PageAsync(owner, project, scope, after, orderByName, size, ct);
            result.AddRange(page);
            if (page.Length < size) return result.ToArray();
            after = new(page[^1].Id, page[^1].Name);
        }
    }
}
