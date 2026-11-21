using Alycia.Modules.Chat.Application;
using System.Runtime.CompilerServices;

namespace Alycia.Modules.Chat.Infrastructure;

internal sealed class EmbeddingJobDiscovery(IConversationIndexOwners conversations, IProjectIndexOwners projectOwners, PostgresEmbeddingStore store, ChatOptions defaults, KnowledgeBuilder? knowledge)
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (knowledge is not null) await knowledge.PurgeExcludedAsync(ct);
        var projects = new Dictionary<Guid, ProjectIndexOwner>();
        await foreach (var project in Pages(projectOwners.ActivePageAsync, p => p.Id, ct)) projects.Add(project.Id, project);
        await foreach (var conversation in Pages(conversations.PageAsync, c => c.Id, ct))
        {
            var options = ConversationConfiguration.Options(conversation.SettingsJson, defaults);
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
    private static async IAsyncEnumerable<T> Pages<T>(Func<Guid?, int, CancellationToken, Task<T[]>> read,
        Func<T, Guid> id, [EnumeratorCancellation] CancellationToken ct)
    {
        const int size = 128;
        Guid? after = null;
        while (true)
        {
            var page = await read(after, size, ct);
            foreach (var item in page) yield return item;
            if (page.Length < size) yield break;
            after = id(page[^1]);
        }
    }
}
