using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class DocumentAnalysisTests
{
    [PostgresFact]
    public async Task NestedArchivesProduceTraceableSummariesAndChangedSourcesInvalidateContext()
    {
        var name = "alycia_analysis_test_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
            p => p.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
        var storage = new FileStorage(Path.Combine(Path.GetTempPath(), "alycia-analysis-" + Guid.NewGuid().ToString("N")));
        try
        {
            await using var database = new ChatDbContext(options);
            await database.Database.MigrateAsync();
            var conversation = Conversation.Create("Analyse", "demo", "system");
            database.Conversations.Add(conversation);
            await database.SaveChangesAsync();
            var source = Encoding.UTF8.GetBytes("public class Sample { }");
            var archive = Zip(("nested.zip", Zip(("src/code.cs", source))), (".env", "SECRET_SHOULD_NOT_LEAK"u8.ToArray()));
            var importer = new FileImport(database, storage);
            var added = await importer.ImportAsync(conversation.Id, Guid.NewGuid(),
                [new("project.zip", new MemoryStream(archive), "2025-01-02T03:04:05Z", "2025-02-03T04:05:06Z", "client-os")], default);
            Assert.Equal(4, added.Count);
            var code = await database.Attachments.SingleAsync(f => f.Name.EndsWith("code.cs"));
            Assert.Equal("project.zip!/nested.zip!/src/code.cs", code.Name);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(source)), code.Sha256);
            Assert.Equal("", code.SourceCreatedAt);
            Assert.Contains("fuseau non fourni", code.MetadataSource);
            var model = new Model();
            var service = new DocumentAnalysisService(database, model, new());
            var report = await service.AnalyzeAsync(conversation.Id, conversation.Revision, null, 1024, null, default);
            Assert.Equal(4, report.FileCount);
            Assert.Contains("Source : tree.txt intégral", string.Join("\n", model.Inputs));
            Assert.DoesNotContain("SECRET_SHOULD_NOT_LEAK", string.Join("\n", model.Inputs));
            Assert.Contains(code.Name, report.TreeText);
            Assert.Contains("sourceFingerprint", report.ManifestJson);
            Assert.Contains("2025-01-02T03:04:05Z", report.TreeText);
            var context = await new EfChatRepository(database).ContextFilesAsync(conversation.Id, 1, default);
            Assert.Single(context);
            Assert.Equal(report.SummaryText, context[0].Text.Split('\n')[1]);
            Assert.False((await service.ListAsync(conversation.Id, default)).Single().IsStale);
            code.ContextMode = "excluded"; conversation.AttachFiles();
            await database.SaveChangesAsync();
            Assert.True((await service.ListAsync(conversation.Id, default)).Single().IsStale);
            Assert.Empty(await new EfChatRepository(database).ContextFilesAsync(conversation.Id, 1, default));

            var unsafeArchive = Zip(("nested.zip", Zip(("../escape.txt", "no"u8.ToArray()))));
            await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(conversation.Id, Guid.NewGuid(),
                [new("unsafe.zip", new MemoryStream(unsafeArchive))], default));
            var deep = Zip(("file.txt", "test"u8.ToArray()));
            for (var i = 0; i < 4; i++) deep = Zip(("nested.zip", deep));
            await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(conversation.Id, Guid.NewGuid(),
                [new("deep.zip", new MemoryStream(deep))], default));
            Assert.Equal(4, await database.Attachments.CountAsync());
            model.Fail = true;
            await Assert.ThrowsAsync<ChatModelException>(() => service.AnalyzeAsync(conversation.Id,
                conversation.Revision, null, 1024, null, default));
            Assert.Equal(1, await database.DocumentAnalyses.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(storage.Root)) Directory.Delete(storage.Root, true);
        }
    }

    private static byte[] Zip(params (string Name, byte[] Bytes)[] files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var file in files)
            {
                using var destination = zip.CreateEntry(file.Name).Open();
                destination.Write(file.Bytes);
            }
        return output.ToArray();
    }

    private sealed class Model : IChatModel
    {
        public List<string> Inputs { get; } = [];
        public bool Fail;
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        {
            if (Fail) throw new ChatModelException("Synthetic model failure");
            Inputs.Add(string.Join("\n", messages.Select(m => m.Content)));
            return Task.FromResult("Résumé synthétique du projet et de ses limites.");
        }
    }
}
