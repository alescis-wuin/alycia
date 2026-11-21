using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapIndex(RouteGroupBuilder routes)
    {
        routes.MapGet("/indexing", async (Guid owner, bool project, IndexCatalog catalog, CancellationToken ct) =>
            Results.Ok(await catalog.ProgressAsync(owner, project, ct)));

        routes.MapGet("/indexing/knowledge", async (Guid owner, bool project, Guid file, string? unit, IndexCatalog catalog, CancellationToken ct) =>
        {
            if (unit is not null && !IsIndexKey(unit)) return Results.BadRequest();
            var node = await catalog.DocumentAsync(owner, project, file, unit, ct);
            return node is null ? Results.NotFound() : Results.Ok(node);
        });

        routes.MapGet("/conversations/{id:guid}/index/knowledge/history", async (Guid id, int? before, string? group, IndexCatalog catalog, CancellationToken ct) =>
        {
            if (before is <= 0 || group is not null && !IsIndexKey(group)) return Results.BadRequest();
            var page = await catalog.HistoryAsync(id, before, group, ct);
            return page is null ? Results.NotFound() : Results.Ok(page);
        });

        routes.MapPost("/indexing", async (QueueIndex request, IndexCatalog catalog, CancellationToken ct) =>
        {
            await catalog.QueueAsync(request, ct);
            return Results.Accepted(value: new { state = "queued" });
        });

        routes.MapPost("/indexing/pause", async (QueueIndex request, IndexCatalog catalog, CancellationToken ct) =>
        {
            await catalog.PauseAsync(request, ct);
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

    private static bool IsIndexKey(string key) => key.Length == 64 && key.All(char.IsAsciiHexDigit);
}
