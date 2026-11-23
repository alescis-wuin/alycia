using System.IO.Compression;
using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Http;

namespace Alycia.Modules.Chat.Tests;

public sealed class FileTests
{
    [Theory]
    [InlineData("Projet test/src/paramètres.json")]
    [InlineData("archive (1).zip")]
    [InlineData("projet/src/data.json")]
    public async Task MultipartFileNamesSurviveDotnetSerializationAndAspnetParsing(string name)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent("{}"u8.ToArray()), "files", name);
        await using var body = new MemoryStream();
        await multipart.CopyToAsync(body);
        body.Position = 0;
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = multipart.Headers.ContentType!.ToString();
        context.Request.ContentLength = body.Length;
        context.Request.Body = body;
        var form = await context.Request.ReadFormAsync();
        var file = Assert.Single(form.Files);
        Assert.Equal(name, file.FileName);
        Assert.Equal(name, FileImport.NormalizeName(file.FileName));
    }

    [PostgresFact]
    public async Task ImportsArchivesPersistsAcrossContextsAndRejectsUnsafeBatchAtomically()
    {
        var name = "alycia_files_test_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
            p => p.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
        var storage = new FileStorage(Path.Combine(Path.GetTempPath(), "alycia-files-" + Guid.NewGuid().ToString("N")));
        Guid conversationId;
        Guid fileId;
        try
        {
            await using (var db = new ChatDbContext(options))
            {
                await db.Database.MigrateAsync();
                var conversation = Conversation.Create("Files", "demo", "system");
                db.Conversations.Add(conversation);
                await db.SaveChangesAsync();
                conversationId = conversation.Id;
                var importer = new FileImport(db, storage, ChatFixture.Profiles(db), new ConversationFileChanges(db));
                var batch = Guid.NewGuid();
                var data = Zip("./folder/./note.txt", "persistent source", windowsDirectory: true);
                var added = await importer.ImportAsync(conversationId, batch, [new("bundle.zip", new MemoryStream(data))], default);
                Assert.Equal(2, added.Count);
                fileId = added.Single(f => f.ArchiveId != null).Id;
                Assert.Equal("bundle.zip!/folder/note.txt", added.Single(f => f.Id == fileId).Name);
                Assert.Equal("persistent source", await File.ReadAllTextAsync(storage.PathFor(conversationId, fileId)));
                var replay = await importer.ImportAsync(conversationId, batch, [new("bundle.zip", new MemoryStream(data))], default);
                Assert.Equal(added.Select(f => f.Id), replay.Select(f => f.Id));
                Assert.Equal(2, await db.Attachments.CountAsync());
                await Assert.ThrowsAsync<ChatConflictException>(() => importer.ImportAsync(conversationId, batch,
                    [new("other.txt", new MemoryStream("different"u8.ToArray()))], default));
                await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(conversationId, Guid.NewGuid(),
                    [new("unsafe.zip", new MemoryStream(Zip("../escape.txt", "no")))], default));
                using var duplicates = new MemoryStream();
                using (var zip = new ZipArchive(duplicates, ZipArchiveMode.Create, true))
                {
                    zip.CreateEntry("note.json");
                    zip.CreateEntry("./note.json");
                }
                await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(conversationId, Guid.NewGuid(),
                    [new("duplicates.zip", new MemoryStream(duplicates.ToArray()))], default));
                var bomb = Zip("too-large.txt", new string('x', FileImport.MaxFileBytes + 1));
                await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(conversationId, Guid.NewGuid(),
                    [new("bomb.zip", new MemoryStream(bomb))], default));
                Assert.Equal(2, await db.Attachments.CountAsync());
            }
            await using (var db = new ChatDbContext(options))
            {
                var file = await db.Attachments.SingleAsync(f => f.Id == fileId);
                Assert.Equal("persistent source", file.Text);
                Assert.True(File.Exists(storage.PathFor(conversationId, fileId)));
                var model = new CaptureModel();
                await ChatFixture.Create(db, model, new()).SendAsync(conversationId, Guid.NewGuid(), "résume", default);
                Assert.Contains("persistent source", model.Prompt![^1].Content);
                var conversation = await db.Conversations.SingleAsync();
                await new EfChatRepository(db, ChatFixture.Profiles(db)).DeleteAsync(conversation, default);
                Assert.Empty(await db.Attachments.ToListAsync());
                storage.DeleteConversation(conversationId);
                Assert.False(Directory.Exists(storage.DirectoryFor(conversationId)));
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(storage.Root)) Directory.Delete(storage.Root, true);
        }
    }

    private static byte[] Zip(string name, string text, bool windowsDirectory = false)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            if (windowsDirectory) archive.CreateEntry(".\\folder\\");
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        return output.ToArray();
    }

    private sealed class CaptureModel : IChatModel
    {
        public IReadOnlyList<PromptMessage>? Prompt { get; private set; }
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        { Prompt = messages; return Task.FromResult("Résumé"); }
    }
}
