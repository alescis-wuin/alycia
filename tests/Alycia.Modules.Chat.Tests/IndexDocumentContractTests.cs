using System.Text;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Testing;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Tests;

public sealed class IndexDocumentContractTests
{
    [PostgresFact]
    public Task Document_index_pages_preserve_cursor_scope_digest_and_exclusion() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var owner = Conversation.Create("fichiers", "local", "");
            var other = Conversation.Create("voisine", "local", "");
            db.AddRange(owner, other);
            await db.SaveChangesAsync();
            var root = Directory.CreateTempSubdirectory("alycia-index-contract-");
            try
            {
                var storage = new FileStorage(root.FullName);
                var importer = new FileImport(db, storage, ChatFixture.Profiles(db), new ConversationFileChanges(db));
                var real = ChatFixture.IndexFiles(db, new ProjectSourceReader(storage));
                var fixtures = new List<IndexDocumentFixture>();
                foreach (var name in new[] { "zeta.txt", "alpha.txt", "exclu.txt" })
                {
                    var text = "Source " + name;
                    var summary = Assert.Single(await importer.ImportAsync(owner.Id, Guid.NewGuid(),
                        [new(name, new MemoryStream(Encoding.UTF8.GetBytes(text)))], default));
                    var file = (await real.FindAsync(owner.Id, false, summary.Id, default))!;
                    var original = new IndexedOriginal(text, true, "original textuel intégral");
                    var flat = real.Split(file, original, false, default).ToArray();
                    var hierarchical = real.Split(file, original, true, default).ToArray();
                    if (name == "exclu.txt")
                    {
                        (await db.Attachments.SingleAsync(f => f.Id == file.Id)).ContextMode = "excluded";
                        await db.SaveChangesAsync();
                        file = file with { ContextMode = "excluded" };
                    }
                    fixtures.Add(new(owner.Id, false, file, original, flat, hierarchical, file.ContextMode == "excluded"));
                }

                var zeta = fixtures[0].File;
                var alpha = fixtures[1].File;
                var excluded = fixtures[2].File;
                foreach (var adapter in new IIndexSourceDocuments[] { real, new SnapshotIndexDocuments(fixtures) })
                {
                    var first = Assert.Single(await adapter.PageAsync(owner.Id, false, null, null, true, 1, default));
                    Assert.Equal(alpha.Id, first.Id);
                    var next = Assert.Single(await adapter.PageAsync(owner.Id, false, null, new(first.Id, first.Name), true, 1, default));
                    Assert.Equal(zeta.Id, next.Id);
                    Assert.Empty(await adapter.PageAsync(owner.Id, false, null, new(next.Id, next.Name), true, 128, default));
                    var byId = await adapter.PageAsync(owner.Id, false, null, null, false, 128, default);
                    Assert.Equal(new[] { alpha.Id, zeta.Id }.Order(), byId.Select(f => f.Id));
                    Assert.Equal(zeta.Id, Assert.Single(await adapter.PageAsync(owner.Id, false, [zeta.Id, excluded.Id], null, true, 128, default)).Id);
                    Assert.Empty(await adapter.PageAsync(owner.Id, false, [], null, true, 1, default));
                    Assert.Empty(await adapter.PageAsync(other.Id, false, null, null, true, 1, default));
                    Assert.Empty(await adapter.PageAsync(owner.Id, true, null, null, true, 1, default));
                    Assert.Null(await adapter.FindAsync(owner.Id, false, excluded.Id, default));
                    Assert.Equal(excluded.Id, Assert.Single(await adapter.ExcludedPageAsync(null, 128, default)));
                    Assert.Empty(await adapter.ExcludedPageAsync(excluded.Id, 128, default));
                    var original = await adapter.ReadAsync(owner.Id, false, alpha.Id, alpha.Sha256, default);
                    Assert.Equal("Source alpha.txt", original.Text);
                    Assert.True(original.Complete);
                    foreach (var hierarchical in new[] { false, true })
                    {
                        var segments = adapter.Split(alpha, original, hierarchical, default);
                        Assert.Contains(segments, s => s.FileId == alpha.Id && s.Sha256 == alpha.Sha256 && s.Text == original.Text);
                    }
                    await Assert.ThrowsAsync<ArgumentException>(() => adapter.ReadAsync(owner.Id, false, alpha.Id, "ancienne", default));
                    await Assert.ThrowsAsync<ArgumentException>(() => adapter.ReadAsync(owner.Id, false, excluded.Id, excluded.Sha256, default));
                    await Assert.ThrowsAsync<ArgumentException>(() => adapter.ReadAsync(other.Id, false, alpha.Id, alpha.Sha256, default));
                    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.PageAsync(owner.Id, false, null, null, true, 129, default));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.PageAsync(owner.Id, false, null, null, true, 1, new(true)));
                }
            }
            finally { root.Delete(true); }
        });
}
