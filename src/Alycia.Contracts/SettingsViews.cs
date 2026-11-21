namespace Alycia.Contracts.Chat;

public interface IGenerationSettings
{
    int ContextTokens { get; }
    int MaxOutputTokens { get; }
    bool GenerateUntilStop { get; }
    double Temperature { get; }
    int? Seed { get; }
}

public interface IIndexSettings
{
    string RetrievalMode { get; }
    string EmbeddingModel { get; }
    int EmbeddingBatchSize { get; }
    int EmbeddingChunkCharacters { get; }
    bool EmbedHistory { get; }
    bool HierarchicalIndexEnabled { get; }
    string EmbeddingDocumentPrefix { get; }
    string EmbeddingQueryPrefix { get; }
    EnrichmentOptions Enrichment { get; }
}

public interface IToolWindowSettings
{
    int ContextTokens { get; }
    int MaxOutputTokens { get; }
    int SafetyTokens { get; }
    int ToolContextTokens { get; }
}
