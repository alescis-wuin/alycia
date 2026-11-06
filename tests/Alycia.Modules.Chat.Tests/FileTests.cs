using System.IO.Compression;
using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class FileTests
{
    [Theory]
    [InlineData("../secret")]
    [InlineData("a/../../secret")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\private")]
    [InlineData("a\0b")]
    public void RejectsUnsafePaths(string path) => Assert.Throws<ArgumentException>(() => FileImport.NormalizeName(path));

    [Fact]
    public void ReadsUtf8AndDocumentsButNeverImages()
    {
        Assert.Equal("été", FileImport.Extract("notes.custom", Encoding.UTF8.GetBytes("été")).Text);
        Assert.Empty(FileImport.Extract("image.svg", Encoding.UTF8.GetBytes("<svg>secret</svg>")).Text);
        Assert.Empty(FileImport.Extract("binary.exe", [0, 1, 2, 255]).Text);
        var docx = Zip("word/document.xml", "<w:document xmlns:w='urn:word'><w:p><w:t>Bonjour document</w:t></w:p></w:document>");
        Assert.Contains("Bonjour document", FileImport.Extract("note.docx", docx).Text);
        var hostile = Zip("word/document.xml", "<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///etc/passwd'>]><x>&ext;</x>");
        Assert.Empty(FileImport.Extract("bad.docx", hostile).Text);
        var large = FileImport.Extract("large.txt", Encoding.UTF8.GetBytes(new string('é', 200000)));
        Assert.True(Encoding.UTF8.GetByteCount(large.Text) <= FileImport.MaxTextBytes);
        Assert.Contains("partiellement", large.Status);
        using var xlsx = new MemoryStream();
        using (var archive = new ZipArchive(xlsx, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("xl/sharedStrings.xml").Open()))
                writer.Write("<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>Budget projet</t></si></sst>");
            using (var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open()))
                writer.Write("<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c r='A1' t='s'><v>0</v></c><c r='B1'><v>42</v></c></row></sheetData></worksheet>");
        }
        var spreadsheet = FileImport.Extract("budget.xlsx", xlsx.ToArray()).Text;
        Assert.Contains("A1: Budget projet", spreadsheet);
        Assert.Contains("B1: 42", spreadsheet);
    }

    [Fact]
    public void FilesFollowSlidingWindowAndRespectBudgetAndRoles()
    {
        var id = Guid.NewGuid();
        var request = Guid.NewGuid();
        var history = new[] { new ChatMessage(id, 1, request, "user", "question"), new ChatMessage(id, 2, request, "assistant", "answer") };
        var old = new Attachment { Id = Guid.NewGuid(), Name = "old.txt", Text = "old content", AnchorSequence = 1 };
        var current = new Attachment { Id = Guid.NewGuid(), Name = "new.txt", Text = new string('é', 10000), AnchorSequence = 3, CreatedAtUtc = DateTimeOffset.UtcNow };
        var options = new ChatOptions { MaxHistoryTurns = 0 };
        var result = FileContext.Build("system", history, "question", options, [old, current], 3);
        Assert.Equal("system", result.Messages[0].Content);
        Assert.Equal("user", result.Messages[^1].Role);
        Assert.DoesNotContain("old content", result.Messages[^1].Content);
        Assert.Contains("new.txt", result.Messages[^1].Content);
        Assert.Equal(1, result.FilesIncluded);
        Assert.True(result.Messages.Sum(m => SlidingContext.EstimateTokens(m.Content))
            <= options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens);
        var next = FileContext.Build("system", [], "later", options, [current], 5);
        Assert.Equal(0, next.FilesIncluded);
        Assert.DoesNotContain("new.txt", next.Messages[^1].Content);
        options.MaxHistoryTurns = 1;
        var recent = new[] { new ChatMessage(id, 5, request, "user", new string('a', 1700)), new ChatMessage(id, 6, request, "assistant", new string('b', 1700)) };
        var expired = FileContext.Build("system", recent, "later", options, [old], 7);
        Assert.Equal(2, expired.HistoryMessagesIncluded);
        Assert.Equal(0, expired.FilesIncluded);
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
                var importer = new FileImport(db, storage);
                var batch = Guid.NewGuid();
                var data = Zip("folder/note.txt", "persistent source");
                var added = await importer.ImportAsync(conversationId, batch, [new("bundle.zip", new MemoryStream(data))], default);
                Assert.Equal(2, added.Count);
                fileId = added.Single(f => f.ArchiveId != null).Id;
                Assert.Equal("persistent source", await File.ReadAllTextAsync(storage.PathFor(conversationId, fileId)));
                var replay = await importer.ImportAsync(conversationId, batch, [new("bundle.zip", new MemoryStream(data))], default);
                Assert.Equal(added.Select(f => f.Id), replay.Select(f => f.Id));
                Assert.Equal(2, await db.Attachments.CountAsync());
                await Assert.ThrowsAsync<ChatConflictException>(() => importer.ImportAsync(conversationId, batch,
                    [new("other.txt", new MemoryStream("different"u8.ToArray()))], default));
                await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(conversationId, Guid.NewGuid(),
                    [new("unsafe.zip", new MemoryStream(Zip("../escape.txt", "no")))], default));
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
                await new ChatService(new EfChatRepository(db), model, new()).SendAsync(conversationId, Guid.NewGuid(), "résume", default);
                Assert.Contains("persistent source", model.Prompt![^1].Content);
                var conversation = await db.Conversations.SingleAsync();
                await new EfChatRepository(db).DeleteAsync(conversation, default);
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

    private static byte[] Zip(string name, string text)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text);
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
