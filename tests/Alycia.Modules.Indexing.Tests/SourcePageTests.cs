namespace Alycia.Modules.Indexing.Tests;

public sealed class SourcePageTests
{
    [Fact]
    public async Task A_two_page_read_retains_its_original_scope_and_uses_the_last_document_as_cursor()
    {
        var first = Guid.NewGuid();
        var allowed = new[] { first };
        var owner = Guid.NewGuid();
        var pages = new Pages(owner, first, () => allowed[0] = Guid.Empty);
        var files = await IndexFiles.SnapshotAsync(pages, owner, false, allowed, true, default);
        Assert.Equal(129, files.Length);
        Assert.Equal(2, pages.Calls);
    }

    private sealed class Pages(Guid expectedOwner, Guid original, Action mutate) : IIndexSourceDocuments
    {
        public int Calls { get; private set; }
        private IndexFileCursor? end;
        public Task<IndexSourceFile[]> PageAsync(Guid owner, bool project, Guid[]? allowed, IndexFileCursor? after, bool byName, int count, CancellationToken ct)
        {
            Assert.Equal((expectedOwner, false, true, 128), (owner, project, byName, count));
            Assert.Equal(original, Assert.Single(allowed!));
            Assert.Equal(end, after);
            var rows = Enumerable.Range(0, Calls++ == 0 ? count : 1).Select(i => new IndexSourceFile(Guid.NewGuid(), i.ToString(),
                "hash", "text", "window", DateTimeOffset.UnixEpoch, "", "", "")).ToArray();
            end = new(rows[^1].Id, rows[^1].Name);
            mutate();
            return Task.FromResult(rows);
        }
        public Task<IndexSourceFile?> FindAsync(Guid owner, bool project, Guid file, CancellationToken ct) => throw new NotSupportedException();
        public Task<IndexedOriginal> ReadAsync(Guid owner, bool project, Guid file, string sha256, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid[]> ExcludedPageAsync(Guid? after, int count, CancellationToken ct) => throw new NotSupportedException();
    }
}
