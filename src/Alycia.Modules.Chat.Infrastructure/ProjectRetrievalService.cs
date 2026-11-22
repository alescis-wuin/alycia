using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Indexing;
namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectRetrievalService(IRetrievalPipeline pipeline, IIndexedEmbeddings? semantic = null) : IProjectRetrieval
{
    public Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ProjectSearch request, CancellationToken ct) =>
        pipeline.PrepareAsync(conversation, query, request.Passages, semantic is null ? null :
            (term, chunks, token) => semantic.SearchAsync(conversation, term, request.Embeddings, chunks, token), ct);

    public static double Cosine(float[] a, float[] b) => VectorScores.Cosine(a, b);
}
