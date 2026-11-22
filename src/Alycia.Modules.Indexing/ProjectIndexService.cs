
namespace Alycia.Modules.Indexing;

public sealed class ProjectIndexService(IConversationIndexOwners conversations, PassageCacheDirectory storage, IIndexSourceDocuments documents, ILiveIndexFileAccess live, IndexParameters? defaults = null, KnowledgeStore? knowledge = null) : IProjectIndex
{
    public Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct) => BuildScopedAsync(conversation, null, ct);
    public async Task<ProjectIndexSnapshot> BuildScopedAsync(Guid conversation, IReadOnlySet<Guid>? allowed, CancellationToken ct)
    {
        var ids = allowed?.ToArray();
        var owner = await conversations.FindAsync(conversation, ct)
            ?? throw new ConversationNotFoundException();
        var settings = owner.Override ?? defaults ?? IndexParameters.Default;
        var maximum = settings.IndexMaxCharacters;
        var files = await IndexFiles.SnapshotAsync(documents, conversation, false, ids, true, ct);
        files = files.OrderByDescending(f => f.ContextMode == "pinned").DistinctBy(f => (f.Name, f.Sha256)).ToArray();
        var chunks = new List<SourceChunk>(); var omissions = new List<string>(); long characters = 0; var indexed = 0; var reused = 0;
        var cache = new ProjectPassageCache(storage, conversation, settings.HierarchicalIndexEnabled);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var parts = await cache.TryReadAsync(file, ct);
                var reusedFile = parts is not null;
                if (parts is null)
                {
                    var original = await documents.ReadAsync(conversation, false, file.Id, file.Sha256, ct);
                    if (maximum > 0 && (long)characters + original.Text.Length > maximum)
                    { omissions.Add(file.Name + " : budget d’index atteint ; original accessible par outils"); continue; }
                    parts = documents.Split(file, original, settings.HierarchicalIndexEnabled, ct).Select(c => c with { CompleteSource = original.Complete }).ToArray();
                    await cache.WriteAsync(file, parts, ct);
                }
                var size = parts.Sum(p => (long)p.Text.Length);
                if (maximum > 0 && (long)characters + size > maximum)
                { omissions.Add(file.Name + " : budget d’index atteint ; original accessible par outils"); continue; }
                if (parts.Any(c => !c.CompleteSource)) omissions.Add(file.Name + " : extraction documentaire partielle");
                if (settings.Enrichment.Enabled && knowledge is not null)
                {
                    var root = await knowledge.DocumentAsync(file.Id, file.Sha256, settings.Enrichment.Profile, ct, settings.HierarchicalIndexEnabled);
                    if (root is null) omissions.Add(file.Name + " · synthèse en attente, passages originaux disponibles");
                    else
                    {
                        var notes = await knowledge.PassagesAsync(file.Id, root, ct);
                        parts = parts.Select(c =>
                        {
                            var reference = SourceCoordinates.Reference(c);
                            if (!notes.TryGetValue(reference, out var leaf)) return c;
                            var terms = IndexedKnowledge.Terms(leaf.Labels) + " " + root.Labels.Title + " " + IndexedKnowledge.Terms(root.Labels)
                                + " " + root.Labels.Summary;
                            return c with
                            {
                                Guide = new(reference, leaf.Labels.Summary, terms, leaf.Identity, leaf.State,
                                file.SourceCreatedAt, file.SourceModifiedAt, file.CreatedAtUtc, leaf.IndexedAt, leaf.Key, leaf.Level, !leaf.Complete)
                            };
                        }).ToArray();
                    }
                }
                if (!await live.IsAllowedAsync(conversation, false, file.Id, ct))
                { omissions.Add(file.Name + " : source ou cache indisponible"); continue; }
                characters += size; chunks.AddRange(parts); indexed++; if (reusedFile) reused++;
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
            { omissions.Add(file.Name + " : source ou cache indisponible"); }
        }
        // Removed/excluded sources cannot remain visible through cache lookup or subsequent retrieval.
        if (allowed is null) cache.RemoveExcept(files.Select(f => f.Id));
        return new(chunks, files.Length, indexed, reused, omissions);
    }
}
