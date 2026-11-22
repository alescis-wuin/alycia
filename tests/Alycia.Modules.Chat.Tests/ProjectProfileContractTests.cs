using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Testing;
using Alycia.Modules.Workspaces.Infrastructure;
using Alycia.Platform.Persistence;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectProfileContractTests
{
    [PostgresFact]
    public Task Catalog_profiles_and_note_origins_enforce_revisions_archives_and_provenance() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var session = new RelationalSession(db.Database);
            var identities = new WorkspaceIdentityDirectory(session);
            var profiles = new WorkspaceProfileStore(session, identities);
            var origins = new ProjectNoteOrigins(db);
            var real = new ProjectCatalog(session, identities, profiles, origins);
            var mock = new MemoryProjectCatalog(new SnapshotNoteOrigins([]), Guid.NewGuid, TimeProvider.System,
                () => db.Database.CurrentTransaction != null);
            foreach (var (catalog, profile) in new (IProjectCatalog, IWorkspaceProfiles)[] { (real, profiles), (mock, mock) })
            {
                var created = await catalog.CreateAsync("  Alpha  ", default);
                Assert.Equal("Alpha", created.Name);
                Assert.Contains(await catalog.ListAsync(default), x => x.Id == created.Id && !x.Archived);
                await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateAsync("alpha", default));
                var id = created.Id;
                Assert.Null(await profile.FindAsync(Guid.Empty, default));
                Assert.Equal(0, (await profile.RequireAsync(id, false, default)).Revision);
                await Assert.ThrowsAsync<InvalidOperationException>(() => profile.LockActiveDestinationAsync(id, default));
                await using (var tx = await db.Database.BeginTransactionAsync())
                {
                    await profile.LockActiveDestinationAsync(id, default);
                    await tx.CommitAsync();
                }
                await profile.AdvanceAsync(id, 0, default);
                await Assert.ThrowsAsync<ChatConflictException>(() => profile.AdvanceAsync(id, 0, default));
                var note = await catalog.ChangeNoteAsync(id, null, 1, "  provenance libre  ", null, null, false, default);
                Assert.Equal("provenance libre", Assert.Single(note.Notes).Text);
                await Assert.ThrowsAsync<ArgumentException>(() => catalog.ChangeNoteAsync(id, null, 2, "refus", Guid.NewGuid(), 1, false, default));
                Assert.Equal(2, (await catalog.GetAsync(id, true, default)).Revision);
                Assert.Empty((await catalog.ChangeNoteAsync(id, note.Notes[0].Id, 2, "", null, null, true, default)).Notes);
                var archived = await catalog.UpdateAsync(id, 3, " x ", " local ", true, default);
                Assert.True(archived.Archived);
                Assert.Equal("local", archived.DefaultModel);
                await Assert.ThrowsAsync<ArgumentException>(() => catalog.ActiveDefaultModelAsync(id, default));
                await Assert.ThrowsAsync<ChatConflictException>(() => profile.AdvanceAsync(id, 4, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => profile.FindAsync(id, new(true)));
            }
            var project = Assert.Single(await real.ListAsync(default)).Id;
            var owner = Conversation.Create("source", "local", "");
            owner.AssignWorkspace(project);
            var request = Guid.NewGuid();
            db.AddRange(owner, new ChatMessage(owner.Id, 1, request, "user", "A"), new ChatMessage(owner.Id, 2, request, "assistant", "B"));
            owner.CompleteTurn();
            await db.SaveChangesAsync();
            foreach (var source in new IProjectNoteOrigins[] { origins, new SnapshotNoteOrigins([(project, owner.Id, 2)]) })
            {
                await source.ValidateAsync(project, owner.Id, 2, default);
                await source.ValidateAsync(project, null, null, default);
                await Assert.ThrowsAsync<ArgumentException>(() => source.ValidateAsync(Guid.Empty, owner.Id, 1, default));
                await Assert.ThrowsAsync<ArgumentException>(() => source.ValidateAsync(project, owner.Id, 3, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ValidateAsync(project, owner.Id, 2, new(true)));
            }
        });
}
