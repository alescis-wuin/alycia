using Alycia.Contracts.Documents;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Testing;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Tests;

public sealed class ArchiveAnalysisContractTests
{
    [PostgresFact]
    public Task Analysis_reads_committed_revisions_and_forks_detach_only_their_own_originals() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var c = Conversation.Create("analyse", ScriptedTextGeneration.Model, "");
            db.Add(c);
            var bytes = "abc"u8.ToArray();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            var attachments = new[] { 1, 5 }.Select(n => new Attachment
            {
                Id = Guid.NewGuid(),
                ConversationId = c.Id,
                Name = "a" + n,
                AnchorSequence = n,
                Sha256 = hash,
                Size = 3,
                BatchId = Guid.NewGuid(),
                Status = "ready"
            }).ToArray();
            db.AddRange(attachments);
            db.Add(new WorkspaceFileOverride { ConversationId = c.Id, FileId = attachments[0].Id, Mode = "excluded" });
            await db.SaveChangesAsync();
            var real = new AnalysisConversationOwner(db, new(), new ScriptedTextGeneration());
            var revision = c.Revision;
            var settings = await real.PreferencesAsync(c.Id, revision, default);
            var memory = new MemoryAnalysisConversations([(ConversationProjection.Summary(c), settings)]);
            foreach (var (port, commit) in new (IAnalysisConversation, Func<Task>)[]
                { (real, async () => await db.SaveChangesAsync()), (memory, () => { memory.Commit(); return Task.CompletedTask; }) })
            {
                Assert.Equal(c.Model, (await port.RequireAsync(c.Id, revision, default)).Model);
                Assert.Equal(settings.Cache.Key("profile"), (await port.PreferencesAsync(c.Id, revision, default)).Cache.Key("profile"));
                await Assert.ThrowsAsync<ConversationNotFoundException>(() => port.RequireAsync(Guid.Empty, null, default));
                await Assert.ThrowsAsync<ChatConflictException>(() => port.PreferencesAsync(c.Id, revision + 1, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.StageAdvanceAsync(c.Id, revision, new(true)));
                Assert.Equal(revision + 1, (await port.StageAdvanceAsync(c.Id, revision, default)).Revision);
                Assert.Equal(revision, (await port.RequireAsync(c.Id, null, default)).Revision);
                await Assert.ThrowsAsync<ChatConflictException>(() => port.StageAdvanceAsync(c.Id, revision, default));
                await commit();
                Assert.Equal(revision + 1, (await port.RequireAsync(c.Id, null, default)).Revision);
            }
            var dir = Directory.CreateTempSubdirectory();
            try
            {
                var disk = new FileStorage(dir.FullName);
                Directory.CreateDirectory(disk.DirectoryFor(c.Id));
                foreach (var f in attachments) await File.WriteAllBytesAsync(disk.PathFor(c.Id, f.Id), bytes);
                var mock = new MemoryArchiveDocuments(attachments.Select(f => new ArchiveFile(c.Id, null, f.Name, hash, f.AnchorSequence)), [(c.Id, attachments[0].Id)]);
                foreach (var (archive, files, staged, modes) in new (IArchiveDocuments, Func<Guid, int>, Func<Guid, int>, Func<Guid, int>)[]
                    { (new ArchiveDocuments(db, disk), id => Directory.Exists(disk.DirectoryFor(id)) ? Directory.GetFiles(disk.DirectoryFor(id)).Length : 0,
                        id => db.ChangeTracker.Entries<Attachment>().Count(e => e.Entity.ConversationId == id),
                        id => db.ChangeTracker.Entries<WorkspaceFileOverride>().Count(e => e.Entity.ConversationId == id)),
                      (mock, id => mock.Originals(id).Length, mock.Attached, mock.Overrides) })
                {
                    Assert.Empty(await archive.DescribeWorkspaceAsync(Guid.NewGuid(), default));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archive.DescribeWorkspaceAsync(c.Id, new(true)));
                    foreach (bool preserve in new[] { false, true })
                    {
                        var destination = Guid.NewGuid();
                        var fork = archive.Fork(c.Id, destination, 1, !preserve);
                        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fork.PrepareAsync(new(true)));
                        await fork.PrepareAsync(default);
                        Assert.Equal(1, files(destination));
                        Assert.Equal(1, staged(destination));
                        Assert.Equal(preserve ? 0 : 1, modes(destination));
                        await Assert.ThrowsAsync<IOException>(() => archive.Fork(c.Id, destination, 1, false).PrepareAsync(default));
                        fork.Detach(!preserve);
                        Assert.Equal(preserve ? 1 : 0, files(destination));
                        Assert.Equal(0, staged(destination));
                        Assert.Equal(0, modes(destination));
                    }
                }
            }
            finally { dir.Delete(true); }
        });
}
