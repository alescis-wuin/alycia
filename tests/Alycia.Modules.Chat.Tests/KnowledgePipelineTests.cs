using Alycia.Modules.Inference;
using Alycia.Modules.Retrieval;
using Alycia.Contracts.Indexing;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

[CollectionDefinition("Foreground indexing", DisableParallelization = true)]
public sealed class ForegroundIndexingCollection { }

[Collection("Foreground indexing")]
public sealed class KnowledgePipelineTests
{
    [PostgresFact]
    public async Task ResumesSmallSlicesPublishesWholeTreesAndRespectsScope()
    {
        var name = "alycia_notes_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
        var dbOptions = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
            pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
        var storage = new FileStorage(Path.Combine(Path.GetTempPath(), name));
        try
        {
            await using var db = new ChatDbContext(dbOptions);
            await db.Database.MigrateAsync();
            var options = new ChatOptions { Enrichment = new(true, "annotation-test", JobCalls: 1, Workers: 2), EmbedHistory = true };
            var owner = Conversation.Create("Notes", "principal", "");
            var other = Conversation.Create("Autre", "principal", "");
            owner.Configure(owner.Model, "", "", ConversationConfiguration.Serialize(options));
            db.Conversations.AddRange(owner, other); await db.SaveChangesAsync();
            var importer = new FileImport(db, storage, ChatFixture.Profiles(db), new ConversationFileChanges(db));
            var files = await importer.ImportAsync(owner.Id, Guid.NewGuid(),
                [Source("alpha.md", "La période alpha couvre 37 jours."), Source("beta.txt", "Le delta couvre 15 jours."),
                 Source("ferme.txt", "NE_PAS_TRANSMETTRE")], default);
            var excluded = await db.Attachments.SingleAsync(f => f.Name == "ferme.txt");
            excluded.ContextMode = "excluded";
            var request = Guid.NewGuid();
            db.Messages.AddRange(new ChatMessage(owner.Id, 1, request, "user", "Le serveur choisi est bleu."),
                new ChatMessage(owner.Id, 2, request, "assistant", "Je propose une couleur indigo."));
            owner.CompleteTurn(); await db.SaveChangesAsync();

            var reader = new ProjectSourceReader(storage);
            var notes = ChatFixture.Knowledge(db, reader);
            var jobs = ChatFixture.Embeddings(db);
            var model = new AnnotationModel();
            var worker = new BackgroundEmbeddingIndex(ChatFixture.IndexFiles(db, reader), ChatFixture.LiveFiles(db), jobs, new NoVectors(), new(), new ConversationIndexOwners(new(db.Database)), ChatFixture.Profiles(db), ChatFixture.Profiles(db), ChatFixture.Foreground, new(notes, model, ChatFixture.Foreground));
            var index = new ProjectIndexService(new ConversationIndexOwners(new(db.Database)), storage, ChatFixture.IndexFiles(db, reader), ChatFixture.LiveFiles(db), options, notes);
            await worker.DiscoverAsync(default);
            async Task<EmbeddingJob> Job() => Assert.Single(await jobs.JobsAsync(default));
            async Task<EmbeddingJob> Complete()
            {
                for (int i = 0; i < 30; i++)
                {
                    var old = await Job(); var calls = model.Prompts.Count;
                    await worker.ProcessAsync(old, default);
                    var current = await Job();
                    Assert.InRange(model.Prompts.Count - calls, 0, 1);
                    Assert.Equal(model.Prompts.Count - calls, current.Progress.CycleCalls);
                    Assert.True(current.Progress.State is "partial" or "completed", current.Progress.Detail);
                    if (current.Progress.State == "completed") return current;
                }
                throw new InvalidOperationException("Le travail ne progresse plus.");
            }
            using (ChatFixture.Foreground.PreferForeground())
            {
                await worker.ProcessAsync(await Job(), default);
                Assert.Equal("queued", (await Job()).Progress.State);
                Assert.Empty(model.Prompts);
            }
            var before = await index.BuildAsync(owner.Id, default);
            Assert.Equal(2, before.Chunks.Count);
            Assert.All(before.Chunks, c => Assert.Null(c.Guide));
            await worker.ProcessAsync(await Job(), default);
            Assert.Equal("partial", (await Job()).Progress.State);
            Assert.Single(model.Prompts);
            var alpha = files.Single(f => f.Name == "alpha.md");
            Assert.Null(await notes.DocumentAsync(alpha.Id, alpha.Sha256, options.Enrichment.Profile, default));

            var done = await Complete();
            Assert.Equal(4, done.Progress.Completed);
            Assert.DoesNotContain(model.Prompts, t => t.Contains("NE_PAS_TRANSMETTRE"));
            var after = await index.BuildAsync(owner.Id, default);
            Assert.Equal(before.Chunks.Select(c => c.Text), after.Chunks.Select(c => c.Text));
            Assert.All(after.Chunks, c => Assert.Equal(ProjectRetrieval.Reference(c), c.Guide!.Reference));
            Assert.Empty((await index.BuildAsync(other.Id, default)).Chunks);
            var root = await notes.DocumentAsync(alpha.Id, alpha.Sha256, options.Enrichment.Profile, default);
            Assert.NotNull(root);
            Assert.Equal("generated", root.State);
            var child = await notes.ChildAsync(alpha.Id, root, Assert.Single(root.Children), default);
            Assert.Equal("section", child!.Level);
            Assert.Null(await notes.ChildAsync(excluded.Id, root, child.Key, default));
            Assert.Null(await notes.ChildAsync(alpha.Id, root, new string('0', 64), default));
            var roles = await db.Database.SqlQueryRaw<string>("SELECT payload->>'role' AS \"Value\" FROM chat.knowledge_units WHERE level='history-group' ORDER BY 1").ToListAsync();
            Assert.Equal(["assistant", "user"], roles);
            var groups = await notes.HistoryPageAsync(owner.Id, options.Enrichment.Profile, int.MaxValue, default);
            Assert.Equal(2, groups.Length);
            var assistant = groups.Single(g => g.Role == "assistant");
            var proposal = Assert.Single(await notes.HistoryChildrenAsync(owner.Id, options.Enrichment.Profile, assistant.Key, default));
            Assert.Equal("assistant_proposal", proposal.AssertionType);
            Assert.Empty(await notes.HistoryChildrenAsync(other.Id, options.Enrichment.Profile, assistant.Key, default));
            var hits = await notes.HistoryAsync(owner.Id, "quasar", options.Enrichment.Profile, 1, DateTimeOffset.UtcNow.AddMinutes(1), default);
            Assert.Single(hits); Assert.Contains(1, hits.Keys);
            Assert.Empty(await notes.HistoryAsync(other.Id, "quasar", options.Enrichment.Profile, 5, DateTimeOffset.UtcNow.AddMinutes(1), default));
            var initialCalls = model.Prompts.Count;
            await jobs.QueueAsync(owner.Id, false, options, default, retry: true);
            await Complete(); Assert.Equal(initialCalls, model.Prompts.Count);

            // Invalid model JSON remains usable. Manual retry replaces only fallback derivatives.
            var added = Assert.Single(await importer.ImportAsync(owner.Id, Guid.NewGuid(), [Source("gamma.txt", "Nouvelle source gamma.")], default));
            model.Invalid = true;
            await worker.DiscoverAsync(default); await Complete();
            var fallback = await notes.DocumentAsync(added.Id, added.Sha256, options.Enrichment.Profile, default);
            Assert.True(fallback!.HasFallback); Assert.Equal("extractive_fallback", fallback.State);
            model.Invalid = false;
            await jobs.QueueAsync(owner.Id, false, options, default, retry: true);
            await Complete();
            var recovered = await notes.DocumentAsync(added.Id, added.Sha256, options.Enrichment.Profile, default);
            Assert.False(recovered!.HasFallback); Assert.NotEqual(fallback.Key, recovered.Key);
            var oldChild = await notes.ChildAsync(added.Id, fallback, fallback.Children[0], default);
            Assert.True(oldChild!.HasFallback);
            Assert.Null(await notes.ChildAsync(added.Id, recovered, oldChild.Key, default));

            // A provider timeout is visible in stored notes and retry replaces only fallback units.
            var timed = Assert.Single(await importer.ImportAsync(owner.Id, Guid.NewGuid(), [Source("lent.txt", "Source à réessayer.")], default));
            model.TimesOut = true;
            await worker.DiscoverAsync(default); await Complete();
            var timedNote = await notes.DocumentAsync(timed.Id, timed.Sha256, options.Enrichment.Profile, default);
            Assert.Equal("timeout_fallback", timedNote!.State);
            Assert.True(timedNote.HasFallback);
            model.TimesOut = false;
            await jobs.QueueAsync(owner.Id, false, options, default, retry: true);
            await Complete();
            var retried = await notes.DocumentAsync(timed.Id, timed.Sha256, options.Enrichment.Profile, default);
            Assert.Equal("generated", retried!.State);
            Assert.False(retried.HasFallback);
            Assert.NotEqual(timedNote.Key, retried.Key);

            var removed = Assert.Single(await importer.ImportAsync(owner.Id, Guid.NewGuid(), [Source("retire.txt", "Retrait pendant la génération.")], default));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.Hold = async () => { entered.TrySetResult(); await release.Task; };
            await worker.DiscoverAsync(default);
            var processing = worker.ProcessAsync(await Job(), default);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using var concurrent = new ChatDbContext(dbOptions);
                var remove = await concurrent.Attachments.SingleAsync(a => a.Id == removed.Id);
                concurrent.Attachments.Remove(remove); await concurrent.SaveChangesAsync();
            }
            finally { release.TrySetResult(); }
            await processing;
            Assert.Null(await notes.DocumentAsync(removed.Id, removed.Sha256, options.Enrichment.Profile, default));
            model.Hold = null;
            var alphaEntity = await db.Attachments.SingleAsync(f => f.Id == alpha.Id);
            alphaEntity.ContextMode = "excluded"; owner.AttachFiles(); await db.SaveChangesAsync();
            Assert.False(await notes.AllowedAsync(owner.Id, false, alpha.Id, default));
            Assert.DoesNotContain((await index.BuildAsync(owner.Id, default)).Chunks, c => c.FileId == alpha.Id);
            await worker.DiscoverAsync(default);
            Assert.Null(await notes.DocumentAsync(alpha.Id, alpha.Sha256, options.Enrichment.Profile, default));
            options.RetrievalMode = "hybrid"; options.EmbeddingModel = "vectors-test"; options.HierarchicalIndexEnabled = true;
            owner.Configure(owner.Model, "", "", ConversationConfiguration.Serialize(options)); await db.SaveChangesAsync();
            var encoder = new TinyVectors();
            var hybrid = new BackgroundEmbeddingIndex(ChatFixture.IndexFiles(db, reader), ChatFixture.LiveFiles(db), jobs, encoder, new(), new ConversationIndexOwners(new(db.Database)), ChatFixture.Profiles(db), ChatFixture.Profiles(db), ChatFixture.Foreground, new(notes, model, ChatFixture.Foreground));
            await hybrid.DiscoverAsync(default);
            for (int iteration = 0; iteration < 40; iteration++)
            {
                var calls = model.Prompts.Count + encoder.Calls;
                await hybrid.ProcessAsync(await Job(), default);
                var current = await Job();
                Assert.InRange(model.Prompts.Count + encoder.Calls - calls, 0, 1);
                Assert.Equal(model.Prompts.Count + encoder.Calls - calls, current.Progress.CycleCalls);
                if (current.Progress.State == "completed") break;
                Assert.Equal("partial", current.Progress.State);
            }
            Assert.Equal("completed", (await Job()).Progress.State);
            var hybridScope = await index.BuildAsync(owner.Id, default);
            var encoded = encoder.Calls;
            var ranked = await hybrid.SearchAsync(owner.Id, "delta", EmbeddingQuery.Capture(options), hybridScope.Chunks, default);
            Assert.Equal(hybridScope.Chunks.Count, ranked.Hits); Assert.Equal(encoded + 1, encoder.Calls);
            await using var deleteScope = new ChatDbContext(dbOptions);
            deleteScope.Conversations.Remove(await deleteScope.Conversations.SingleAsync(c => c.Id == owner.Id));
            await deleteScope.SaveChangesAsync();
            Assert.Empty(await db.Database.SqlQueryRaw<string>("SELECT key AS \"Value\" FROM chat.knowledge_units").ToListAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(storage.Root)) Directory.Delete(storage.Root, true);
        }
    }

    [Fact]
    public void FacetsPreserveConstraintsAndAnUnexaminedCandidateKeepsItsRank()
    {
        const string question = "Compare Atlas 2.1 puis cherche QuaiMax sans réseau.";
        var plan = QueryPlanning.Build(question, [], [], 4, true);
        var brief = plan.Brief!;
        Assert.Equal(question, brief.Original);
        Assert.Equal(2, brief.Facets.Length);
        Assert.All(brief.Facets, f => { Assert.Contains("sans réseau", f.Semantic); Assert.Contains("sans réseau", f.Lexical); });
        Assert.Contains("2.1", brief.Literals);
        SourceChunk Chunk(string content) => new(Guid.NewGuid(), "", StructuredMemory.Hash(content), "", "text", 0, content.Length, 1, 1, content, [], true);
        var examined = Chunk("Aucune donnée exploitable.");
        var next = Chunk("Atlas version 2.1");
        var classified = PassageClassification.Apply(question, brief, [examined, next], new() { ClassificationBudget = 4 });
        Assert.Equal(4, classified.Report.Pairs);
        Assert.Equal(1, classified.Report.Unexamined);
        Assert.Equal("irrelevant", Assert.Single(classified.Report.Candidates).Relevance);
        Assert.Same(next, Assert.Single(classified.Ranked));
    }

    [Fact]
    public void AnnotationValidationRejectsForeignReferencesAndOverBudgetSummaries()
    {
        var options = new ChatOptions();
        string[] refs = ["SABCDEF123456"];
        var original = IndexedKnowledge.Extract("test", "Fait daté 2025-02-16 🧭.", "document", refs, 128, options);
        Assert.Contains("2025-02-16", original.DateMentions);
        string Encode(UnitLabels note) => JsonSerializer.Serialize(note, ProjectToolCatalog.Json);
        Assert.NotNull(IndexedKnowledge.Validate(Encode(original), refs, 128, options));
        Assert.Null(IndexedKnowledge.Validate(Encode(original with { SourceUnitIds = ["H99:FOREIGN"] }), refs, 128, options));
        Assert.Null(IndexedKnowledge.Validate(Encode(original with { Summary = new string('a', 800) }), refs, 64, options));
        Assert.Null(IndexedKnowledge.Validate("{\"unsupported\":true}", refs, 128, options));
    }

    [Fact]
    public async Task InteractiveReductionUsesTheEntireSelectedOriginalOrKeepsOnlyTheExcerpt()
    {
        var text = new string('a', 1400) + "milieu" + new string('z', 1590) + "fin";
        var file = Guid.NewGuid();
        var chunk = new SourceChunk(file, "texte.md", StructuredMemory.Hash(text), "section", "text", 0, text.Length, 1, 1, text, [], true);
        var settings = new ChatOptions
        {
            Enrichment = new(true, "fixture", InputTokens: 2048, OutputTokens: 256, TargetTokens: 128),
            QuerySummaryTokens = 512,
            QuerySummaryMaxCalls = 1,
            SourceCandidates = [chunk]
        };
        var excerpt = new SourceExcerpt(file, chunk.Path, 8, true, "budget", ProjectRetrieval.Reference(file, chunk.Sha256, 0, 8),
            1, 1, chunk.Sha256, 0, 8, Content: text[..8]);
        var window = new ContextWindow([new("user", text[..8])], 0, 24, 1, Sources: [excerpt]);
        var model = new SummaryModel();
        var meter = new MeteredChatModel(model);
        Assert.Same(window, await ContextGuides.AddAsync(window, settings, meter, true, default));
        Assert.Empty(model.Inputs);
        settings.QuerySummaryMaxCalls = 8;
        var withGuide = await ContextGuides.AddAsync(window, settings, meter, true, default);
        var guide = Assert.Single(withGuide.Guides!);
        Assert.Equal("on_demand", guide.State);
        Assert.Equal(ProjectRetrieval.Reference(chunk), guide.OriginalReference);
        Assert.Equal(text, string.Concat(model.Inputs.SkipLast(1)));
        Assert.InRange(model.Inputs.Count, 3, 8);
        Assert.InRange(guide.Tokens, 1, 512);
    }

    [Fact]
    public async Task RecursiveAnalysisCheckpointsInsideOneLargeFile()
    {
        var model = new SummaryModel();
        var options = new ChatOptions { ContextTokens = 2048, MaxOutputTokens = 256, SafetyTokens = 256, SummaryMaxCalls = 8, ModelIdentity = "fixture" };
        var cache = new Dictionary<string, string>();
        RecursiveSummarizer Reducer() => new(model, "fixture", options, null, new(options.SummaryMaxCalls),
            (key, _) => Task.FromResult(cache.GetValueOrDefault(key)), (key, value, _) => { cache.Add(key, value); return Task.CompletedTask; });
        var source = new SummarySource("grand.md", string.Join('\n', Enumerable.Range(0, 430).Select(n => $"Ligne {n:D3} avec des valeurs distinctes pour les contrôles.")));
        await Assert.ThrowsAsync<ChatModelException>(() => Reducer().ReduceAsync([source], 512, default));
        Assert.Equal(8, cache.Count);
        string? final = null;
        for (var cycle = 0; cycle < 12 && final is null; cycle++)
        {
            var count = cache.Count;
            try { final = await Reducer().ReduceAsync([source], 512, default); }
            catch (ChatModelException error) { Assert.Contains("Budget d’appels", error.Message); }
            Assert.InRange(cache.Count - count, 1, options.SummaryMaxCalls);
        }
        Assert.NotNull(final);
        Assert.Equal(model.Inputs.Count, model.Inputs.Distinct().Count());
    }

    private sealed class SummaryModel : IChatModel
    {
        public List<string> Inputs { get; } = [];
        public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct) =>
            Task.FromResult(new ModelTokenization("fixture"));
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["fixture"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        {
            Inputs.Add(messages[^1].Content);
            return Task.FromResult("Résumé " + StructuredMemory.Hash(messages[^1].Content)[..16]);
        }
    }

    private static IncomingFile Source(string name, string text) => new(name, new MemoryStream(Encoding.UTF8.GetBytes(text)));

    private sealed class NoVectors : ILocalEmbeddings
    {
        public Task<string> IdentityAsync(string model, CancellationToken ct) => throw new InvalidOperationException();
        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct) => throw new InvalidOperationException();
    }

    private sealed class TinyVectors : ILocalEmbeddings
    {
        public int Calls;
        public Task<string> IdentityAsync(string model, CancellationToken ct) => Task.FromResult("vectors-test:digest");
        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new EmbeddingBatch(input.Select(_ => new float[] { 1, 0 }).ToArray(), null));
        }
    }

    private sealed class AnnotationModel : IChatModel
    {
        public ConcurrentQueue<string> Prompts { get; } = new();
        public bool Invalid;
        public bool TimesOut;
        public Func<Task>? Hold;
        public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct) =>
            Task.FromResult(new ModelTokenization("fixture:digest"));
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["annotation-test"]);
        public async Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        {
            if (Hold is { } hold) await hold();
            var text = messages[^1].Content;
            Prompts.Enqueue(text);
            if (TimesOut) throw new ChatModelException("Délai de synthèse dépassé.", failure: ChatModelFailure.Timeout);
            if (Invalid) return "{\"summary\":\"inventé\"}";
            var ids = JsonSerializer.Deserialize<string[]>(text.Split("sourceUnitIds=")[1].Split('\n')[0])!;
            var result = new UnitLabels("Note", "", ["quasar"], [], [], "texte", [], "fr", "Sujet quasar", [], [], [], ids);
            return JsonSerializer.Serialize(result, ProjectToolCatalog.Json);
        }
    }
}
