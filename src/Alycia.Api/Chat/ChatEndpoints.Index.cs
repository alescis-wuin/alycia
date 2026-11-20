using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapIndex(RouteGroupBuilder routes)
    {
        routes.MapGet("/indexing", async (Guid owner, bool project, ChatDbContext database, PostgresEmbeddingStore store, CancellationToken ct) =>
        {
            if (project) await RequireProjectAsync(database, owner, ct);
            else if (!await database.Conversations.AnyAsync(c => c.Id == owner, ct)) throw new ConversationNotFoundException();
            var jobs = await store.JobsAsync(ct);
            return Results.Ok(jobs.Where(j => j.Progress.OwnerId == owner && j.Progress.IsProject == project).Select(j => j.Progress));
        });
        routes.MapGet("/indexing/knowledge", async (Guid owner, bool project, Guid file, string? unit, ChatDbContext database,
            KnowledgeStore knowledge, PostgresEmbeddingStore jobs, ChatOptions defaults, CancellationToken ct) =>
        {
            if (unit is not null && (unit.Length != 64 || !unit.All(char.IsAsciiHexDigit))) return Results.BadRequest();
            EnrichmentOptions? options; bool hierarchical;
            var sources = database.ModelSources(owner);
            if (project)
            {
                await RequireProjectAsync(database, owner, ct);
                var settings = (await jobs.JobsAsync(ct)).FirstOrDefault(j => j.Progress.IsProject && j.Progress.OwnerId == owner)?.Settings;
                if (settings is null) return Results.NotFound();
                options = settings.Enrichment; hierarchical = settings.Hierarchical;
                sources = database.Attachments.AsNoTracking().Where(f => f.WorkspaceId == owner);
            }
            else
            {
                var conversation = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == owner, ct);
                if (conversation is null) return Results.NotFound();
                var settings = ConversationConfiguration.Options(conversation, defaults);
                options = settings.Enrichment; hierarchical = settings.HierarchicalIndexEnabled;
            }
            var source = await sources.Where(f => f.Id == file && f.ContextMode != "excluded").Select(f => new { f.Sha256 }).SingleOrDefaultAsync(ct);
            if (options?.Enabled != true || source is null) return Results.NotFound();
            var root = await knowledge.DocumentAsync(file, source.Sha256, options.Profile, ct, hierarchical);
            if (root is null) return Results.NotFound();
            var node = unit is null ? root : await knowledge.ChildAsync(file, root, unit, ct);
            return node is null ? Results.NotFound() : Results.Ok(node);
        });
        routes.MapGet("/conversations/{id:guid}/index/knowledge/history", async (Guid id, int? before, string? group, ChatDbContext database,
            KnowledgeStore knowledge, ChatOptions defaults, CancellationToken ct) =>
        {
            if (before is <= 0 || group is not null && (group.Length != 64 || !group.All(char.IsAsciiHexDigit))) return Results.BadRequest();
            var owner = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
            if (owner is null) return Results.NotFound();
            var settings = ConversationConfiguration.Options(owner, defaults);
            if (!settings.Enrichment.Enabled || !settings.EmbedHistory) return Results.Ok(Array.Empty<IndexedNote>());
            return Results.Ok(group is null ? await knowledge.HistoryPageAsync(id, settings.Enrichment.Profile, before ?? int.MaxValue, ct)
                : await knowledge.HistoryChildrenAsync(id, settings.Enrichment.Profile, group, ct));
        });
        routes.MapPost("/indexing", async (QueueIndex request, ChatDbContext database, PostgresEmbeddingStore store,
            ChatOptions defaults, CancellationToken ct) =>
        {
            ChatOptions options;
            if (request.Project)
            {
                var profile = await RequireProjectAsync(database, request.Owner, ct);
                if (profile.Archived) throw new ArgumentException("Le projet est archivé.");
                options = defaults.Copy();
                options.EmbeddingModel = request.Model?.Trim() ?? options.EmbeddingModel;
                if (request.Enrichment is not null) options.Enrichment = request.Enrichment;
                if (options.EmbeddingModel.Length > 0) options.RetrievalMode = "hybrid";
                options.HierarchicalIndexEnabled = request.Hierarchical ?? options.HierarchicalIndexEnabled;
                options.EmbeddingDocumentPrefix = request.DocumentPrefix ?? options.EmbeddingDocumentPrefix;
                options.EmbeddingQueryPrefix = request.QueryPrefix ?? options.EmbeddingQueryPrefix;
                if (request.BatchSize.HasValue) options.EmbeddingBatchSize = request.BatchSize.Value;
                if (request.ChunkCharacters.HasValue) options.EmbeddingChunkCharacters = request.ChunkCharacters.Value;
            }
            else
            {
                var conversation = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.Owner, ct)
                    ?? throw new ConversationNotFoundException();
                options = ConversationConfiguration.Options(conversation, defaults);
                if (!options.Enrichment.Enabled && options.RetrievalMode != "hybrid") throw new ArgumentException("Activez la recherche hybride dans les réglages de la conversation.");
            }
            if (!options.Enrichment.Enabled && string.IsNullOrWhiteSpace(options.EmbeddingModel)) throw new ArgumentException("Choisissez un modèle local d’embeddings.");
            await store.QueueAsync(request.Owner, request.Project, options, ct, retry: true);
            return Results.Accepted(value: new { state = "queued" });
        });
        routes.MapPost("/indexing/pause", async (QueueIndex request, ChatDbContext database, PostgresEmbeddingStore store, CancellationToken ct) =>
        {
            if (request.Project) await RequireProjectAsync(database, request.Owner, ct);
            else if (!await database.Conversations.AnyAsync(c => c.Id == request.Owner, ct)) throw new ConversationNotFoundException();
            await store.PauseAsync(request.Owner, request.Project, ct);
            return Results.NoContent();
        });
        routes.MapGet("/conversations/{id:guid}/index", async (Guid id, string? query, int? offset, IProjectIndex index, CancellationToken ct) =>
        {
            if (query?.Length > 200 || offset is < 0) throw new ArgumentException("Recherche d’index invalide.");
            var snapshot = await index.BuildAsync(id, ct);
            var results = snapshot.Chunks.Where(c => string.IsNullOrEmpty(query)
                || c.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            var start = offset ?? 0;
            return Results.Ok(new
            {
                snapshot.EligibleFiles,
                snapshot.IndexedFiles,
                snapshot.ReusedFiles,
                snapshot.Omissions,
                totalChunks = snapshot.Chunks.Count,
                matchingChunks = results.Length,
                nextOffset = start + 50 < results.Length ? (int?)(start + 50) : null,
                symbols = results.Skip(start).Take(50).Select(c => new { c.FileId, c.Path, c.Sha256, c.Symbol, c.Kind, c.Start, c.End, c.StartLine, c.EndLine, c.References, c.CompleteUnit })
            });
        });
    }

    private sealed record QueueIndex(Guid Owner, bool Project, string? Model, int? BatchSize = null, int? ChunkCharacters = null,
        bool? Hierarchical = null, string? DocumentPrefix = null, string? QueryPrefix = null, EnrichmentOptions? Enrichment = null);
}
