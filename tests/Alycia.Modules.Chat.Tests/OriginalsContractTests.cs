using Alycia.Contracts.Documents;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Testing;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Tests;

public sealed class OriginalsContractTests
{
    [PostgresFact]
    public Task Original_readers_and_revision_staging_distinguish_missing_private_unavailable_and_foreign() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var owner = Conversation.Create("A", "local", "");
            var other = Conversation.Create("B", "local", "");
            db.AddRange(owner, other);
            byte[] bytes = "abc"u8.ToArray();
            var files = new[] { "dir/a.txt", "missing.txt" }.Select(name => new Attachment
            {
                Id = Guid.NewGuid(),
                ConversationId = owner.Id,
                BatchId = Guid.NewGuid(),
                Name = name,
                Size = bytes.Length,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                ContextMode = "excluded",
                Status = "ready",
                CreatedAtUtc = DateTimeOffset.UtcNow
            }).ToArray();
            db.AddRange(files);
            db.AddRange(new WorkspaceFileOverride { ConversationId = owner.Id, FileId = files[0].Id, Mode = "excluded" },
                new WorkspaceFileOverride { ConversationId = other.Id, FileId = files[0].Id, Mode = "excluded" });
            await db.SaveChangesAsync();
            using var temporary = new TemporaryStorage();
            var storage = new FileStorage(temporary.Path);
            Directory.CreateDirectory(storage.DirectoryFor(owner.Id));
            await File.WriteAllBytesAsync(storage.PathFor(owner.Id, files[0].Id), bytes);
            var originals = new SnapshotOriginals([new(owner.Id, files[0].Id, files[0].Name, DateTimeOffset.UtcNow, bytes),
                new(owner.Id, files[1].Id, files[1].Name, DateTimeOffset.UtcNow, null)]);
            bytes[0] = 0;
            foreach (var read in new IConversationOriginals[] { new ConversationOriginals(db, storage, ChatFixture.DocumentScope), originals })
            {
                Assert.Null(await read.DownloadAsync(other.Id, files[0].Id, default));
                Assert.Null(await read.DownloadAsync(owner.Id, Guid.Empty, default));
                var download = Assert.IsType<DownloadedOriginal>(await read.DownloadAsync(owner.Id, files[0].Id, default));
                using var contents = new StreamReader(download.Content);
                Assert.Equal("a.txt", download.Name);
                Assert.Equal("abc", await contents.ReadToEndAsync());
                await Assert.ThrowsAsync<OriginalUnavailableException>(() => read.DownloadAsync(owner.Id, files[1].Id, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.DownloadAsync(owner.Id, files[0].Id, new(true)));
            }
            var mock = new MemoryFileChanges([new(owner.Id, null, owner.Revision, 0)]);
            var start = owner.Revision;
            foreach (var (change, commit) in new (IConversationFileChanges, Func<Task>)[]
                { (new ConversationFileChanges(db), async () => await db.SaveChangesAsync()),
                  (mock, () => { mock.Commit(); return Task.CompletedTask; }) })
            {
                Assert.Equal(start, (await change.RequireAsync(owner.Id, default)).Revision);
                await change.StageAdvanceAsync(owner.Id, start, default);
                Assert.Equal(start, (await change.RequireAsync(owner.Id, default)).Revision);
                await Assert.ThrowsAsync<ChatConflictException>(() => change.StageAdvanceAsync(owner.Id, start, default));
                await commit();
                Assert.Equal(start + 1, (await change.RequireAsync(owner.Id, default)).Revision);
                await Assert.ThrowsAsync<ConversationNotFoundException>(() => change.RequireAsync(Guid.Empty, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change.RequireAsync(owner.Id, new(true)));
            }
            var modes = new MemoryDocumentOverrides([(owner.Id, files[0].Id), (other.Id, files[0].Id)]);
            foreach (var (changes, contains) in new (IDocumentMembershipOverrides, Func<Guid, Task<bool>>)[]
                { (new DocumentMembershipOverrides(db), o => db.WorkspaceFileOverrides.AsNoTracking().AnyAsync(m => m.ConversationId == o && m.FileId == files[0].Id)),
                  (modes, o => Task.FromResult(modes.Contains(o, files[0].Id))) })
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changes.ClearAsync(owner.Id, new(true)));
                Assert.True(await contains(owner.Id));
                await changes.ClearAsync(owner.Id, default);
                await changes.ClearAsync(owner.Id, default);
                Assert.False(await contains(owner.Id));
                Assert.True(await contains(other.Id));
            }
        });

    private sealed class TemporaryStorage : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("alycia-originals-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
