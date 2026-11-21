using Alycia.Modules.Workspaces.Infrastructure;
using System.IO.Compression;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectFilesTests
{
    [PostgresFact]
    public async Task SharedArchiveRespectsScopesRevisionsAndRetainsOriginalsAfterRemoval()
    {
        var database = "alycia_project_files_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        var storage = new FileStorage(Path.Combine(Path.GetTempPath(), database));
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
            await using var db = new ChatDbContext(options);
            await db.Database.MigrateAsync();
            var project = new WorkspaceProfile { Id = Guid.NewGuid() };
            var chat = Conversation.Create("Membre", "demo", "");
            chat.AssignWorkspace(project.Id);
            var outsider = Conversation.Create("Privée", "demo", "");
            db.WorkspaceProfiles.Add(project);
            db.Conversations.AddRange(chat, outsider);
            await db.SaveChangesAsync();

            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("preuve.txt").Open()))
                writer.Write("Source : échantillon.");
            archive.Position = 0;
            var importer = new FileImport(db, storage, ChatFixture.Profiles(db), new ConversationFileChanges(db));
            var files = new ProjectFiles(db, importer, new(storage), ChatFixture.Profiles(db), new ConversationFileChanges(db), ChatFixture.DocumentScope);
            var original = await importer.ImportAsync(chat.Id, Guid.NewGuid(), [new("lot.zip", archive)], default);
            var archiveId = original.Single(f => f.ArchiveId is null).Id;
            var shared = await files.ShareAsync(chat.Id, archiveId, default);
            var root = shared.Single(f => f.ArchiveId is null);
            var child = shared.Single(f => f.ArchiveId == root.Id);
            Assert.Equal(project.Id, child.WorkspaceId);
            Assert.Equal(shared.Select(f => f.Id).Order(), (await files.ShareAsync(chat.Id, archiveId, default)).Select(f => f.Id).Order());
            Assert.Equal(shared.OrderBy(f => f.Name).Select(f => f.Id), (await files.ListAsync(project.Id, default)).Select(f => f.Id));
            Assert.Null(await files.PreviewAsync(outsider.Id, false, child.Id, null, null, null, default));
            Assert.Null(await files.PreviewAsync(project.Id, true, archiveId, null, null, null, default));
            var preview = await files.PreviewAsync(chat.Id, false, child.Id, child.Sha256.ToUpperInvariant(), 9, 20, default);
            Assert.Equal(new SourcePreview(child.Name, child.Sha256, "échantillon", 9, 20, true), preview);
            await Assert.ThrowsAsync<ArgumentException>(() => files.PreviewAsync(project.Id, true, child.Id, "ancienne", null, null, default));

            await db.Entry(project).ReloadAsync();
            Assert.Equal(1, project.Revision);
            var revision = project.Revision;
            await Assert.ThrowsAsync<ChatConflictException>(() => files.SetModeAsync(project.Id, child.Id, "pinned", revision - 1, default));
            Assert.Equal("pinned", (await files.SetModeAsync(project.Id, child.Id, "pinned", revision, default)).ContextMode);
            await db.Entry(project).ReloadAsync();
            Assert.Equal(revision + 1, project.Revision);
            db.WorkspaceFileOverrides.Add(new() { ConversationId = chat.Id, FileId = child.Id, Mode = "excluded" });
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<ArgumentException>(() => files.PreviewAsync(chat.Id, false, child.Id, null, null, null, default));
            await files.ClearOverrideAsync(chat.Id, child.Id, chat.Revision, default);
            Assert.NotNull(await files.PreviewAsync(chat.Id, false, child.Id, null, null, null, default));
            project.Archived = true;
            await db.SaveChangesAsync();
            Assert.Equal(2, (await files.ListAsync(project.Id, default)).Length);
            await Assert.ThrowsAsync<ArgumentException>(() => files.RemoveAsync(project.Id, root.Id, project.Revision, default));
            project.Archived = false;
            await db.SaveChangesAsync();

            var childPath = storage.PathFor(await db.Attachments.AsNoTracking().SingleAsync(f => f.Id == child.Id));
            Assert.False(await files.RemoveAsync(project.Id, archiveId, project.Revision, default));
            Assert.True(await files.RemoveAsync(project.Id, root.Id, project.Revision, default));
            Assert.Empty(await files.ListAsync(project.Id, default));
            Assert.True(File.Exists(childPath));
            Assert.Equal(2, await db.Attachments.CountAsync(f => f.ConversationId == chat.Id));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(storage.Root)) Directory.Delete(storage.Root, true);
        }
    }
}
