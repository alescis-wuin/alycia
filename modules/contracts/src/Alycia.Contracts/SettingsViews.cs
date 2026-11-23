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

public readonly record struct ToolWindowParameters(int ContextTokens, int MaxOutputTokens, int SafetyTokens,
    int ToolContextTokens) : IToolWindowSettings
{
    public static ToolWindowParameters Capture(IToolWindowSettings value) =>
        new(value.ContextTokens, value.MaxOutputTokens, value.SafetyTokens, value.ToolContextTokens);
}

public readonly record struct MemoryBudgetLimits(int History, int Memory)
{
    public (int History, int Memory) Within(int available) =>
        (History > 0 ? Math.Min(available, History) : available, Memory > 0 ? Math.Min(available, Memory) : available);
}
