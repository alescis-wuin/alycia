using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Indexing;

public sealed record IndexParameters(string RetrievalMode, string EmbeddingModel, int EmbeddingBatchSize,
    int EmbeddingChunkCharacters, bool EmbedHistory, bool HierarchicalIndexEnabled, string EmbeddingDocumentPrefix,
    string EmbeddingQueryPrefix, EnrichmentOptions Enrichment, int IndexMaxCharacters) : IIndexSettings
{
    public static IndexParameters Default { get; } = Capture(new ChatSettings());

    public static IndexParameters Capture(ChatSettings settings) => new(settings.RetrievalMode, settings.EmbeddingModel,
        settings.EmbeddingBatchSize, settings.EmbeddingChunkCharacters, settings.EmbedHistory, settings.HierarchicalIndexEnabled,
        settings.EmbeddingDocumentPrefix, settings.EmbeddingQueryPrefix, settings.Enrichment, settings.IndexMaxCharacters);

    public void Validate()
    {
        Enrichment.Validate();
        if (!Valid(this, IndexMaxCharacters))
            throw new ArgumentException("Configuration Chat invalide : vérifiez fenêtre, contexte, réserve et délai.");
    }

    internal static bool Valid(IIndexSettings value, int maxCharacters) =>
        value.RetrievalMode is "legacy" or "lexical" or "hybrid" && value.EmbeddingModel is { Length: <= 200 }
        && value.EmbeddingBatchSize is >= 1 and <= 64 && value.EmbeddingChunkCharacters is >= 128 and <= 3200
        && value.EmbeddingDocumentPrefix is { Length: <= 256 } && value.EmbeddingQueryPrefix is { Length: <= 256 }
        && maxCharacters >= 0;
}
