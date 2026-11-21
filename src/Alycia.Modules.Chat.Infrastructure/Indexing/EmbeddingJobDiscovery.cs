using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

internal sealed class EmbeddingJobDiscovery(ChatDbContext database, PostgresEmbeddingStore store, ChatOptions defaults, KnowledgeBuilder? knowledge)
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (knowledge is not null) await knowledge.PurgeExcludedAsync(ct);
        var conversations = await database.Conversations.AsNoTracking().ToArrayAsync(ct);
        var projects = await database.WorkspaceProfiles.AsNoTracking().Where(p => !p.Archived).ToDictionaryAsync(p => p.Id, ct);
        foreach (var conversation in conversations)
        {
            var options = ConversationConfiguration.Options(conversation, defaults);
            if (EmbeddingSettings.RequiresJob(options))
                await store.QueueAsync(conversation.Id, false, options, ct, sourceRevision: conversation.Revision + ":"
                    + (conversation.WorkspaceId is Guid project && projects.TryGetValue(project, out var profile) ? profile.Revision : 0));
        }
        if (EmbeddingSettings.RequiresJob(defaults))
        {
            var existing = (await store.JobsAsync(ct)).Where(j => j.Progress.IsProject).Select(j => j.Progress.OwnerId).ToHashSet();
            foreach (var project in projects.Values.Where(p => !existing.Contains(p.Id)))
                await store.QueueAsync(project.Id, true, defaults, ct, sourceRevision: project.Revision.ToString());
        }
        foreach (var job in (await store.JobsAsync(ct)).Where(j => j.Progress.IsProject))
        {
            if (!projects.TryGetValue(job.Progress.OwnerId, out var project)) { await store.DeleteJobAsync(job, ct); continue; }
            var options = defaults.Copy(); options.EmbeddingModel = job.Settings.Model; options.EmbeddingBatchSize = job.Settings.BatchSize;
            options.EmbeddingChunkCharacters = job.Settings.ChunkCharacters; options.EmbedHistory = job.Settings.History;
            options.Enrichment = job.Settings.Enrichment ?? new();
            if (job.Settings.Model.Length > 0) options.RetrievalMode = "hybrid";
            options.HierarchicalIndexEnabled = job.Settings.Hierarchical;
            options.EmbeddingDocumentPrefix = job.Settings.DocumentPrefix; options.EmbeddingQueryPrefix = job.Settings.QueryPrefix;
            await store.QueueAsync(project.Id, true, options, ct, sourceRevision: project.Revision.ToString());
        }
    }
}
