using Alycia.Modules.Chat.Application;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapIndex(RouteGroupBuilder routes)
    {
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
}
