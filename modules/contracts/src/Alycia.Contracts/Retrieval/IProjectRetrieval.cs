using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Retrieval;

public sealed record ProjectSearch(RetrievalRequest Passages, EmbeddingQuery Embeddings);

public interface IProjectRetrieval
{
    Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ProjectSearch request, CancellationToken ct);
}
