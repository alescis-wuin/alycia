using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record QueueIndex(Guid Owner, bool Project, string? Model, int? BatchSize = null, int? ChunkCharacters = null,
    bool? Hierarchical = null, string? DocumentPrefix = null, string? QueryPrefix = null, EnrichmentOptions? Enrichment = null);

public sealed class IndexCatalog(ChatDbContext database, PostgresEmbeddingStore store, KnowledgeStore knowledge, ChatOptions defaults, IWorkspaceProfiles profiles)
{
    private async Task RequireOwnerAsync(Guid owner, bool project, CancellationToken ct)
    {
        if (project) await profiles.RequireAsync(owner, false, ct);
        else if (!await database.Conversations.AnyAsync(c => c.Id == owner, ct)) throw new ConversationNotFoundException();
    }

    public async Task<EmbeddingProgress[]> ProgressAsync(Guid owner, bool project, CancellationToken ct)
    {
        await RequireOwnerAsync(owner, project, ct);
        var jobs = await store.JobsAsync(ct);
        return jobs.Where(j => j.Progress.OwnerId == owner && j.Progress.IsProject == project).Select(j => j.Progress).ToArray();
    }

    public async Task<IndexedNote?> DocumentAsync(Guid owner, bool project, Guid file, string? unit, CancellationToken ct)
    {
        EnrichmentOptions? options; bool hierarchical;
        var sources = database.ModelSources(owner);
        if (project)
        {
            await profiles.RequireAsync(owner, false, ct);
            var settings = (await store.JobsAsync(ct)).FirstOrDefault(j => j.Progress.IsProject && j.Progress.OwnerId == owner)?.Settings;
            if (settings is null) return null;
            options = settings.Enrichment; hierarchical = settings.Hierarchical;
            sources = database.Attachments.AsNoTracking().Where(f => f.WorkspaceId == owner);
        }
        else
        {
            var conversation = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == owner, ct);
            if (conversation is null) return null;
            var settings = ConversationConfiguration.Options(conversation, defaults);
            options = settings.Enrichment; hierarchical = settings.HierarchicalIndexEnabled;
        }
        var source = await sources.Where(f => f.Id == file && f.ContextMode != "excluded").Select(f => new { f.Sha256 }).SingleOrDefaultAsync(ct);
        if (options?.Enabled != true || source is null) return null;
        var root = await knowledge.DocumentAsync(file, source.Sha256, options.Profile, ct, hierarchical);
        if (root is null) return null;
        return unit is null ? root : await knowledge.ChildAsync(file, root, unit, ct);
    }

    public async Task<IndexedNote[]?> HistoryAsync(Guid id, int? before, string? group, CancellationToken ct)
    {
        var owner = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (owner is null) return null;
        var settings = ConversationConfiguration.Options(owner, defaults);
        if (!settings.Enrichment.Enabled || !settings.EmbedHistory) return [];
        return group is null ? await knowledge.HistoryPageAsync(id, settings.Enrichment.Profile, before ?? int.MaxValue, ct)
            : await knowledge.HistoryChildrenAsync(id, settings.Enrichment.Profile, group, ct);
    }

    public async Task QueueAsync(QueueIndex request, CancellationToken ct)
    {
        ChatOptions options;
        if (request.Project)
        {
            var profile = await profiles.RequireAsync(request.Owner, false, ct);
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
    }

    public async Task PauseAsync(QueueIndex request, CancellationToken ct)
    {
        await RequireOwnerAsync(request.Owner, request.Project, ct);
        await store.PauseAsync(request.Owner, request.Project, ct);
    }
}
