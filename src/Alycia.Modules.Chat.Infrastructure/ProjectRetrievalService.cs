using System.Collections.Frozen;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectRetrievalService(IRetrievalPipeline pipeline, IIndexedEmbeddings? semantic = null) : IProjectRetrieval
{
    public Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ChatOptions settings, CancellationToken ct)
    {
        var options = settings.Copy();
        var request = new RetrievalRequest(options.RetrievalMode, options.EligibleSourceIds?.ToFrozenSet(),
            PassageRankingSettings.Capture(options, options.PinnedSourceIds),
            options.QueryRewriteEnabled || options.FacetPlanningEnabled || options.QueryBriefModelEnabled, options.SearchPlan);
        return pipeline.PrepareAsync(conversation, query, request, semantic is null ? null :
            (term, chunks, token) => semantic.SearchAsync(conversation, term, options, chunks, token), ct);
    }

    public static double Cosine(float[] a, float[] b) => VectorSimilarity.Cosine(a, b);
}
