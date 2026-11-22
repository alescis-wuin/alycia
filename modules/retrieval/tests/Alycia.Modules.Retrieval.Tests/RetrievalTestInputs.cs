using System.Collections.Frozen;

namespace Alycia.Modules.Chat.Tests;

internal static class RetrievalTestInputs
{
    internal static Alycia.Contracts.Documents.TurnFile File(string name, string mode) => new(
        Guid.Empty, null, null, Guid.Empty, null, name, 0, "", "", "", 0, default, "", "", "unknown", mode);

    internal static ProjectRetrievalService Service(IProjectIndex index, IIndexedEmbeddings? semantic = null,
        LexicalIndexCache? lexicalCache = null, IPassageReranker? reranker = null) =>
        new(new RetrievalPipeline(index, lexicalCache ?? new(), reranker), semantic);

    internal static ProjectSearch ProjectSearch(this ChatSettings settings, IReadOnlySet<Guid>? eligible = null,
        IReadOnlySet<Guid>? pinned = null, SearchQueryPlan? plan = null) =>
        new(new(settings.RetrievalMode, eligible?.ToFrozenSet(), PassageRankingSettings.Capture(settings, pinned),
            settings.QueryRewriteEnabled || settings.FacetPlanningEnabled || settings.QueryBriefModelEnabled, plan),
            EmbeddingQuery.Capture(settings));
}
