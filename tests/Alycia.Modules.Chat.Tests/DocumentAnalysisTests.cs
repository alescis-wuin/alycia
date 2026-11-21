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
            var service = new DocumentAnalysisService(database, model, new(), new ProjectSourceReader(storage), ChatFixture.Gate);
            var report = await service.AnalyzeAsync(conversation.Id, conversation.Revision, null, 1024, null, default);
            Assert.Equal(4, report.FileCount);
            Assert.Contains("Source : tree.txt intégral", string.Join("\n", model.Inputs));
            Assert.DoesNotContain("SECRET_SHOULD_NOT_LEAK", string.Join("\n", model.Inputs));
            Assert.Contains(code.Name, report.TreeText);
            Assert.Contains("sourceFingerprint", report.ManifestJson);
            Assert.Contains("2025-01-02T03:04:05Z", report.TreeText);
            var context = await new EfChatRepository(database).ContextFilesAsync(conversation.Id, 1, default);
            Assert.Equal(2, context.Count);
            Assert.Contains(context, f => f.Id == code.Id);
            Assert.EndsWith(report.SummaryText, Assert.Single(context, f => f.Id == report.Id).Text);
            Assert.False((await service.ListAsync(conversation.Id, default)).Single().IsStale);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync(conversation.Id,
                conversation.Revision, null, 1024, status => status.StartsWith("Réduction hiérarchique", StringComparison.Ordinal)
                    ? Task.FromException(new OperationCanceledException()) : Task.CompletedTask, default));
            var interrupted = Assert.Single(await service.RunsAsync(conversation.Id, default), r => r.State == "cancelled");
            Assert.Equal(1, interrupted.CompletedUnits);
            var resumed = await service.AnalyzeAsync(conversation.Id, conversation.Revision, null, 1024, null, default, interrupted.Id);
            Assert.Equal(1, (await service.RunsAsync(conversation.Id, default)).Single(r => r.Id == interrupted.Id).ReusedUnits);
            Assert.Equal(resumed.Id, (await service.AnalyzeAsync(conversation.Id, conversation.Revision, null, 1024, null, default, interrupted.Id)).Id);

            using (service.AcquireAnalysisLease(conversation.Id))
            {
                await Assert.ThrowsAsync<ChatConflictException>(() => service.DeleteRunAsync(conversation.Id, interrupted.Id, default));
                await Assert.ThrowsAsync<ChatConflictException>(() => service.DeleteAsync(conversation.Id, report.Id, conversation.Revision, default));
            }
            var missing = Guid.NewGuid();
            Assert.Null(await service.GetAsync(missing, report.Id, default));
            await Assert.ThrowsAsync<ChatConflictException>(() => service.SelectAsync(conversation.Id, missing, conversation.Revision - 1, true, default));
            Assert.Null(await service.SelectAsync(conversation.Id, missing, conversation.Revision, true, default));
            var revision = conversation.Revision;
            var selected = await service.SelectAsync(conversation.Id, report.Id, revision, true, default);
            Assert.Equal(revision + 1, selected!.Revision);
            Assert.Equal(report.Id, Assert.Single(await service.ListAsync(conversation.Id, default), a => a.UseInContext).Id);

            code.ContextMode = "excluded"; conversation.AttachFiles();
            await database.SaveChangesAsync();
            Assert.All(await service.ListAsync(conversation.Id, default), a => Assert.True(a.IsStale));
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
            Assert.Equal(2, await database.DocumentAnalyses.CountAsync());

            model.Fail = false;
            var optionsForSlice = new ChatOptions { SummaryMaxCalls = 8, Enrichment = new(true, Workers: 2) };
            conversation.Configure(conversation.Model, conversation.SystemPrompt, "", ConversationConfiguration.Serialize(optionsForSlice));
            await database.SaveChangesAsync();
            var many = Enumerable.Range(0, 10).Select(i => new IncomingFile($"doc-{i:D2}.md",
                new MemoryStream(Encoding.UTF8.GetBytes($"Document numéro {i}, contenu original distinct.")))).ToArray();
            var batch = Guid.NewGuid();
            await importer.ImportAsync(conversation.Id, batch, many, default);
            var beforeSlice = model.Inputs.Count;
            await Assert.ThrowsAsync<ChatModelException>(() => service.AnalyzeAsync(conversation.Id, conversation.Revision, batch, 1024, null, default));
            Assert.Equal(8, model.Inputs.Count - beforeSlice);
            var partial = Assert.Single(await service.RunsAsync(conversation.Id, default), r => r.ScopeBatchId == batch);
            Assert.Equal("partial", partial.State);
            var finished = await service.AnalyzeAsync(conversation.Id, conversation.Revision, batch, 1024, null, default, partial.Id);
            Assert.Equal(10, finished.FileCount);
            Assert.InRange(finished.SummaryCalls, 1, 8);
            Assert.True((await service.RunsAsync(conversation.Id, default)).Single(r => r.Id == partial.Id).ReusedUnits > 0);
            var inventory = await service.InventoryAsync(conversation.Id, batch, default);
            Assert.Contains("doc-00.md", inventory);
            Assert.DoesNotContain(code.Name, inventory);
            await service.DeleteRunAsync(conversation.Id, partial.Id, default);
            Assert.DoesNotContain(await service.RunsAsync(conversation.Id, default), r => r.Id == partial.Id);
            Assert.Equal(finished.Id, (await service.GetAsync(conversation.Id, finished.Id, default))!.Id);
            Assert.True(await service.DeleteAsync(conversation.Id, finished.Id, conversation.Revision, default));
            Assert.False(await service.DeleteAsync(conversation.Id, finished.Id, conversation.Revision, default));
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
        public System.Collections.Concurrent.ConcurrentQueue<string> Inputs { get; } = new();
        public bool Fail;
        public Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions options, CancellationToken ct)
        {
            var copy = options.Copy(); copy.ModelIdentity = "synthetic:digest-v1"; return Task.FromResult(copy);
        }
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        {
            if (Fail) throw new ChatModelException("Synthetic model failure");
            Inputs.Enqueue(string.Join("\n", messages.Select(m => m.Content)));
            return Task.FromResult("Résumé synthétique du projet et de ses limites.");
        }
    }
}
