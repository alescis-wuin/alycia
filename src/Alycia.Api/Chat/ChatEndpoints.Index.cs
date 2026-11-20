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
                if (options.RetrievalMode != "hybrid") throw new ArgumentException("Activez la recherche hybride dans les réglages de la conversation.");
            }
            if (string.IsNullOrWhiteSpace(options.EmbeddingModel)) throw new ArgumentException("Choisissez un modèle local d’embeddings.");
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
        bool? Hierarchical = null, string? DocumentPrefix = null, string? QueryPrefix = null);
}
