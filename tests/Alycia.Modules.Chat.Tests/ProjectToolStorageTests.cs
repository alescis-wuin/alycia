using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectToolStorageTests
{
    [PostgresFact]
    public async Task MigrationOriginalReadsIsolationExclusionsCoverageAndRevocation()
    {
        var name = "alycia_tools_test_" + Guid.NewGuid().ToString("N");
        // Npgsql removes the password from ConnectionString after OpenAsync.
        var testConnection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(testConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(testConnection)
        { Database = name, Pooling = false }.ToString(), p => p.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
        var storage = new FileStorage(Path.Combine(Path.GetTempPath(), "alycia-tools-" + Guid.NewGuid().ToString("N")));
        try
        {
            await using var db = new ChatDbContext(options);
            await db.Database.MigrateAsync();
            var conversation = Conversation.Create("Projet", "demo", "system");
            var other = Conversation.Create("Autre", "demo", "");
            db.Conversations.AddRange(conversation, other); await db.SaveChangesAsync();
            var original = "🙂\n" + new string('x', 200100) + "\nclass HiddenAtEnd { int Value => 42; int ValueOf() => Value; }\n";
            var added = await new FileImport(db, storage, ChatFixture.Profiles(db), new ConversationFileChanges(db)).ImportAsync(conversation.Id, Guid.NewGuid(),
                [new("src/A.cs", new MemoryStream(Encoding.UTF8.GetBytes(original))), new(".env", new MemoryStream("SECRET_NEVER_READ"u8.ToArray()))], default);
            // Seed through the current importer, then exercise the upgrade from the old schema.
            // No current EF conversation mapping is used while the new column is absent.
            await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Take(5).Last());
            await db.Database.ExecuteSqlRawAsync("UPDATE chat.attachments SET \"ContextMode\" = 'window' WHERE \"Name\" = 'src/A.cs'");
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            conversation = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
            var file = await db.Attachments.SingleAsync(f => f.Name == "src/A.cs");
            var excluded = await db.Attachments.SingleAsync(f => f.Name == ".env");
            Assert.Null(conversation.WorkspaceId);
            Assert.Null(file.WorkspaceId);
            Assert.Equal(conversation.Id, file.ConversationId);
            Assert.Equal(added.Single(f => f.Name == "src/A.cs").Id, file.Id);
            Assert.Equal(original, await File.ReadAllTextAsync(storage.PathFor(file)));
            Assert.Equal("project", file.ContextMode); Assert.Equal("excluded", excluded.ContextMode);
            Assert.DoesNotContain("HiddenAtEnd", file.Text);
            Assert.Single(await new EfChatRepository(db, ChatFixture.Profiles(db)).ContextFilesAsync(conversation.Id, 99, default));
            var reader = new ProjectSourceReader(storage);
            var index = new ProjectIndexService(new ConversationIndexOwners(new(db.Database)), storage, ChatFixture.IndexFiles(db, reader), ChatFixture.LiveFiles(db));
            var indexed = await index.BuildAsync(conversation.Id, default);
            Assert.Equal(1, indexed.IndexedFiles);
            Assert.Contains(indexed.Chunks, c => c.Symbol.Contains("HiddenAtEnd"));
            Assert.DoesNotContain(indexed.Chunks, c => c.Text.Contains("SECRET_NEVER_READ"));
            Assert.Equal(1, (await index.BuildAsync(conversation.Id, default)).ReusedFiles);
            var cachePath = Path.Combine(storage.DirectoryFor(conversation.Id), "index-v1", file.Id.ToString("N") + ".json");
            var cacheJson = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(cachePath))!;
            cacheJson["chunks"] = null;
            await File.WriteAllTextAsync(cachePath, cacheJson.ToJsonString());
            var repaired = await index.BuildAsync(conversation.Id, default);
            Assert.Equal(0, repaired.ReusedFiles);
            Assert.Contains(repaired.Chunks, c => c.Symbol.Contains("HiddenAtEnd"));
            var selected = ProjectRetrieval.Rank(indexed.Chunks, "HiddenAtEnd Value");
            var context = FileContext.Build("system", [], "HiddenAtEnd Value", new() { SourceCandidates = selected }, [file], 1);
            Assert.Contains("HiddenAtEnd", context.Messages[^1].Content);
            Assert.Contains(context.Sources!, s => s.Start > 200000 && s.Reference is not null);
            Assert.Empty((await index.BuildAsync(other.Id, default)).Chunks);
            file.ContextMode = "excluded"; await db.SaveChangesAsync();
            Assert.Empty((await index.BuildAsync(conversation.Id, default)).Chunks);
            file.ContextMode = "project"; await db.SaveChangesAsync();
            Assert.Equal(0, (await index.BuildAsync(conversation.Id, default)).ReusedFiles);
            var history = new EfChatRepository(db, ChatFixture.Profiles(db));
            var policy = new ToolConversationPolicyReader(db);
            var tools = new ProjectToolService(new AuthorizedToolSources(db, reader, policy, new(), ChatFixture.LiveFiles(db)), policy, history, new(), new ProjectIndexService(new ConversationIndexOwners(new(db.Database)), storage, ChatFixture.IndexFiles(db, reader), ChatFixture.LiveFiles(db), new()));
            Task<ProjectToolResult> Call(string tool, object args, Guid? scope = null, int budget = 2048) =>
                tools.CallAsync(scope ?? conversation.Id, tool, JsonSerializer.SerializeToElement(args), budget, default);
            var found = await Call("project_search", new { query = "hiddenatend" });
            Assert.False(found.IsError); Assert.Contains("HiddenAtEnd", found.Text);
            using var foundJson = JsonDocument.Parse(found.Text);
            var offset = foundJson.RootElement.GetProperty("matches")[0].GetProperty("offset").GetInt32();
            var read = await Call("project_read", new { fileId = file.Id, offset });
            var passages = await Call("project_search_passages", new { query = "HiddenAtEnd" }, budget: 8192);
            Assert.False(passages.IsError); Assert.Contains("HiddenAtEnd", passages.Text);
            Assert.NotEmpty(passages.EvidenceItems!);
            var declarations = await Call("project_find_symbols", new { query = "HiddenAtEnd.Value" }, budget: 8192);
            Assert.False(declarations.IsError);
            var many = await Call("project_read_many", new { reads = new[] { new { fileId = file.Id, offset }, new { fileId = excluded.Id, offset = 0 } } }, budget: 8192);
            Assert.False(many.IsError); Assert.Single(many.EvidenceItems!);
            Assert.DoesNotContain("SECRET_NEVER_READ", many.Text);
            Assert.Empty((await Call("project_search_passages", new { query = "HiddenAtEnd" }, other.Id)).EvidenceItems!);
            Assert.False(read.IsError); Assert.Contains("HiddenAtEnd", read.Text); Assert.True(read.Evidence!.CompleteSource);
            Assert.Equal(file.Sha256, read.Evidence.Sha256);
            Assert.InRange(Encoding.UTF8.GetByteCount(read.Text), 1, 2048);
            var symbol = await Call("project_read", new { fileId = file.Id, symbol = "HiddenAtEnd.Value" });
            Assert.False(symbol.IsError);
            using var symbolJson = JsonDocument.Parse(symbol.Text);
            Assert.Equal("int Value => 42;", symbolJson.RootElement.GetProperty("text").GetString());
            Assert.True(symbol.Evidence!.Offset > 200000);
            Assert.Equal(JsonValueKind.Null, symbolJson.RootElement.GetProperty("nextOffset").ValueKind);
            var ambiguous = await Call("project_read", new { fileId = file.Id, symbol = "Value" });
            Assert.True(ambiguous.IsError); Assert.Contains("HiddenAtEnd.ValueOf", ambiguous.Text);
            Assert.True((await Call("project_read", new { fileId = file.Id, symbol = "HiddenAtEnd.Value", offset = 0 })).IsError);
            Assert.True((await Call("project_read", new { fileId = file.Id }, other.Id)).IsError);
            Assert.True((await Call("project_read", new { fileId = excluded.Id })).IsError);
            Assert.True((await Call("project_read", new { fileId = "../../etc/passwd" })).IsError);
            Assert.True((await Call("project_read", new { fileId = file.Id, offset = 1 })).IsError);
            Assert.True((await Call("project_list", new { offset = "wrong" })).IsError);
            Assert.True((await Call("project_list", new { path = "/tmp" })).IsError);
            Assert.True((await Call("run_shell", new { })).IsError);
            var page = await Call("project_list", new { limit = 1 });
            using var pageJson = JsonDocument.Parse(page.Text);
            Assert.Equal(1, pageJson.RootElement.GetProperty("nextOffset").GetInt32());
            var model = new SummaryModel();
            var report = await new DocumentAnalysisService(db, model, new(), reader, ChatFixture.Gate).AnalyzeAsync(conversation.Id, conversation.Revision, null, 1024, null, default);
            Assert.Contains(model.Inputs, s => s.Contains("HiddenAtEnd"));
            Assert.DoesNotContain(model.Inputs, s => s.Contains("SECRET_NEVER_READ"));
            Assert.InRange(Encoding.UTF8.GetByteCount(report.SummaryText), 1, 1024);
            using var manifest = JsonDocument.Parse(report.ManifestJson);
            var coverage = manifest.RootElement.GetProperty("coverage");
            Assert.True(coverage.GetProperty("allEligibleTextRead").GetBoolean());
            Assert.Equal(1, coverage.GetProperty("codeFilesSummarized").GetInt32());
            Assert.Equal(1, coverage.GetProperty("excludedFiles").GetInt32());
            Assert.Equal(original.Length, manifest.RootElement.GetProperty("fileSummaries").EnumerateArray()
                .Single(f => f.GetProperty("path").GetString() == "src/A.cs").GetProperty("charactersRead").GetInt32());
            Assert.False((await Call("project_read", new { fileId = file.Id, offset })).IsError);
            await using (var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
            {
                Assert.False((await Call("project_read", new { fileId = file.Id, offset })).IsError);
                await using var fresh = new ChatDbContext(options);
                await fresh.Attachments.Where(f => f.Id == file.Id).ExecuteUpdateAsync(set => set.SetProperty(f => f.ContextMode, "excluded"));
                Assert.True((await Call("project_read", new { fileId = file.Id, offset })).IsError);
                await snapshot.RollbackAsync();
                await fresh.Attachments.Where(f => f.Id == file.Id).ExecuteUpdateAsync(set => set.SetProperty(f => f.ContextMode, "project"));
            }
            conversation.Configure("demo", "system", "", ConversationConfiguration.Serialize(new() { UseProjectTools = false }));
            await db.SaveChangesAsync();
            Assert.True((await Call("project_read", new { fileId = file.Id })).IsError);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(storage.Root)) Directory.Delete(storage.Root, true);
        }
    }
    private sealed class SummaryModel : IChatModel
    {
        public List<string> Inputs { get; } = [];
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        { Inputs.Add(string.Join("\n", messages.Select(m => m.Content))); return Task.FromResult("Résumé du code et de ses limites."); }
    }
}
