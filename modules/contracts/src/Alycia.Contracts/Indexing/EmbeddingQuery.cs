using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Indexing;

public sealed record EmbeddingQuery(string Model, int BatchSize, int ChunkCharacters, bool Hierarchical,
    string DocumentPrefix, string QueryPrefix)
{
    public static EmbeddingQuery Capture(IIndexSettings source) =>
        new(source.EmbeddingModel, source.EmbeddingBatchSize, source.EmbeddingChunkCharacters,
            source.HierarchicalIndexEnabled, source.EmbeddingDocumentPrefix, source.EmbeddingQueryPrefix);
}
