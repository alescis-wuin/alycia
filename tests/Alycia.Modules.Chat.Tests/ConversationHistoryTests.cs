using System.IO.Compression;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class ConversationHistoryTests
{
    [PostgresFact]
    public async Task ForkScopesFilesOverridesAndHistoryAndRemovesIncompleteCopies()
    {
        var name = "alycia_history_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var storage = new FileStorage(Path.Combine(Path.GetTempPath(), name));
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
            await using var db = new ChatDbContext(options);
            await db.Database.MigrateAsync();
            var history = new ConversationHistory(db, new(), storage);
            var importer = new FileImport(db, storage, ChatFixture.Profiles(db));
            var project = new WorkspaceProfile { Id = Guid.NewGuid() };
            db.WorkspaceProfiles.Add(project);
            var date = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
            var archive = new ConversationArchive(1, "Plan", "demo", "", "Notes", new(),
                [new("user", "Préfixe %_ exact", date), new("assistant", "Reçu", date.AddSeconds(1))]);
            var source = await history.ImportAsync(ConversationTransfer.ExportJson(archive), default);
            source.AssignWorkspace(project.Id);
            db.Conversations.Add(Conversation.Create("Plan privé", "demo", ""));
            await db.SaveChangesAsync();
            using var zip = new MemoryStream();
            using (var bundle = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(bundle.CreateEntry("texte.txt").Open()))
                writer.Write("Document antérieur");
            zip.Position = 0;
            var files = await importer.ImportAsync(source.Id, Guid.NewGuid(), [new("pièces.zip", zip)], default);
            var shared = Assert.Single(await importer.ImportAsync(project.Id, Guid.NewGuid(),
                [new("partagé.txt", new MemoryStream("projet"u8.ToArray()))], default, workspace: true));
            db.WorkspaceFileOverrides.Add(new() { ConversationId = source.Id, FileId = shared.Id, Mode = "excluded" });
            var turn = Guid.NewGuid();
            source.CompleteTurn();
            await new EfChatRepository(db, ChatFixture.Profiles(db)).AppendTurnAsync(source, new(source.Id, 3, turn, "user", "Ensuite"),
                new(source.Id, 4, turn, "assistant", "Suite"), default);
            await importer.ImportAsync(source.Id, Guid.NewGuid(), [new("tardif.txt", new MemoryStream("après"u8.ToArray()))], default);

            var results = await history.SearchAsync("%_", 0, null, null, default);
            Assert.Equal(source.Id, Assert.Single(results.Items).Id);
            Assert.False(results.HasMore);
            Assert.Empty((await history.SearchAsync("%_", 0, null, true, default)).Items);
            await Assert.ThrowsAsync<ChatConflictException>(() => history.ForkAsync(source.Id, source.Revision - 1, 1, default));
            await Assert.ThrowsAsync<ArgumentException>(() => history.ForkAsync(source.Id, source.Revision, 1, default));
            var copy = await history.ForkAsync(source.Id, source.Revision, 2, default);
            var exported = await history.ArchiveAsync(copy.Id, default);
            Assert.Equal(archive.Messages, exported.Messages);
            Assert.Equal("Copie · Plan", exported.Title);
            Assert.Equal(project.Id, exported.SourceWorkspaceId);
            Assert.NotNull(exported.ReferencedProjectFiles);
            Assert.Equal(shared.Name + " · " + shared.Sha256, Assert.Single(exported.ReferencedProjectFiles));
            Assert.Equal("excluded", (await db.WorkspaceFileOverrides.FindAsync(copy.Id, shared.Id))!.Mode);
            var copied = await db.Attachments.AsNoTracking().Where(f => f.ConversationId == copy.Id).ToArrayAsync();
            var root = Assert.Single(copied, f => f.ArchiveId is null);
            var child = Assert.Single(copied, f => f.ArchiveId == root.Id);
            Assert.Equal(files.Single(f => f.ArchiveId.HasValue).Sha256, child.Sha256);
            Assert.Equal("Document antérieur", await File.ReadAllTextAsync(storage.PathFor(child)));
            Assert.DoesNotContain(copied, f => f.Name == "tardif.txt");

            File.Delete(storage.PathFor(source.Id, files.Single(f => f.ArchiveId.HasValue).Id));
            var directories = Directory.GetDirectories(storage.Root).Order().ToArray();
            await Assert.ThrowsAsync<FileNotFoundException>(() => history.ForkAsync(source.Id, source.Revision, 2, default));
            Assert.Equal(directories, Directory.GetDirectories(storage.Root).Order());
            Assert.Equal(3, await db.Conversations.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(storage.Root)) Directory.Delete(storage.Root, true);
        }
    }
}
