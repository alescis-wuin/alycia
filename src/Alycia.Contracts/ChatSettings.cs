namespace Alycia.Contracts.Chat;

public class ChatSettings : IGenerationSettings, IIndexSettings, IToolWindowSettings
{
    public bool StructuredMemory { get; set; } = true;
    public string RetrievalMode { get; set; } = "lexical";
    public string EmbeddingModel { get; set; } = "";
    public string RoutingMode { get; set; } = "off";
    public bool TemporalMemoryEnabled { get; set; }
    public bool AdaptiveInferenceEnabled { get; set; }
    public bool ClaimVerificationEnabled { get; set; }
    public bool VerificationSearchEnabled { get; set; }
    public int AgentPlanMaxCalls { get; set; } = 8;
    public bool AgentPlanningEnabled { get; set; }
    public bool QueryRewriteEnabled { get; set; }
    public int MaxSearchQueries { get; set; } = 3;
    public EnrichmentOptions Enrichment { get; set; } = new();
    public bool FacetPlanningEnabled { get; set; }
    public bool QueryBriefModelEnabled { get; set; }
    public bool ClassificationEnabled { get; set; }
    public int ClassificationBudget { get; set; } = 120;
    public int NeutralCandidates { get; set; } = 4;
    public double RelevantThreshold { get; set; } = .45;
    public double NeutralThreshold { get; set; } = .12;
    public int QuerySummaryMaxCalls { get; set; } = 1;
    public int QuerySummaryTokens { get; set; } = 0;
    public int MemoryContextTokens { get; set; }
    public int HistoryContextTokens { get; set; }
    public bool HierarchicalIndexEnabled { get; set; }
    public string EmbeddingDocumentPrefix { get; set; } = "";
    public string EmbeddingQueryPrefix { get; set; } = "";
    public bool EvidenceSelectionEnabled { get; set; }
    public bool RerankEnabled { get; set; }
    public int RerankCandidateLimit { get; set; } = 24;
    public int RerankTimeoutMs { get; set; } = 1500;
    public string TokenCounting { get; set; } = "auto";
    public bool UseProjectTools { get; set; } = true;
    // A call limit is an optional execution guard, never a token budget.
    public int ToolMaxCalls { get; set; } = 0;
    public int ToolContextTokens { get; set; } = 0;
    public int ToolResultBytes { get; set; } = 24000;
    public int ToolTimeoutSeconds { get; set; } = 1200;
    public int DocumentContextTokens { get; set; } = 0;
    public int IndexMaxCharacters { get; set; } = 0;
    public int EmbeddingBatchSize { get; set; } = 16;
    public int EmbeddingChunkCharacters { get; set; } = 1600;
    public bool EmbedHistory { get; set; } = true;
    public bool AutoSummarize { get; set; } = true;
    public int SummaryMaxCalls { get; set; } = 256;
    public bool GenerateUntilStop { get; set; } = true;
    public int MaxHistoryTurns { get; set; } = 12;
    public int ProtectedHistoryTurns { get; set; } = 2;
    public int ContextTokens { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 1024;
    public int SafetyTokens { get; set; } = 256;
    public int TimeoutSeconds { get; set; } = 180;
    public double Temperature { get; set; } = 0.7;
    public int? Seed { get; set; }

    public ChatSettings() { }

    protected ChatSettings(ChatSettings source)
    {
        StructuredMemory = source.StructuredMemory;
        RetrievalMode = source.RetrievalMode;
        EmbeddingModel = source.EmbeddingModel;
        RoutingMode = source.RoutingMode;
        TemporalMemoryEnabled = source.TemporalMemoryEnabled;
        AdaptiveInferenceEnabled = source.AdaptiveInferenceEnabled;
        ClaimVerificationEnabled = source.ClaimVerificationEnabled;
        VerificationSearchEnabled = source.VerificationSearchEnabled;
        AgentPlanMaxCalls = source.AgentPlanMaxCalls;
        AgentPlanningEnabled = source.AgentPlanningEnabled;
        QueryRewriteEnabled = source.QueryRewriteEnabled;
        MaxSearchQueries = source.MaxSearchQueries;
        Enrichment = source.Enrichment;
        FacetPlanningEnabled = source.FacetPlanningEnabled;
        QueryBriefModelEnabled = source.QueryBriefModelEnabled;
        ClassificationEnabled = source.ClassificationEnabled;
        ClassificationBudget = source.ClassificationBudget;
        NeutralCandidates = source.NeutralCandidates;
        RelevantThreshold = source.RelevantThreshold;
        NeutralThreshold = source.NeutralThreshold;
        QuerySummaryMaxCalls = source.QuerySummaryMaxCalls;
        QuerySummaryTokens = source.QuerySummaryTokens;
        MemoryContextTokens = source.MemoryContextTokens;
        HistoryContextTokens = source.HistoryContextTokens;
        HierarchicalIndexEnabled = source.HierarchicalIndexEnabled;
        EmbeddingDocumentPrefix = source.EmbeddingDocumentPrefix;
        EmbeddingQueryPrefix = source.EmbeddingQueryPrefix;
        EvidenceSelectionEnabled = source.EvidenceSelectionEnabled;
        RerankEnabled = source.RerankEnabled;
        RerankCandidateLimit = source.RerankCandidateLimit;
        RerankTimeoutMs = source.RerankTimeoutMs;
        TokenCounting = source.TokenCounting;
        UseProjectTools = source.UseProjectTools;
        ToolMaxCalls = source.ToolMaxCalls;
        ToolContextTokens = source.ToolContextTokens;
        ToolResultBytes = source.ToolResultBytes;
        ToolTimeoutSeconds = source.ToolTimeoutSeconds;
        DocumentContextTokens = source.DocumentContextTokens;
        IndexMaxCharacters = source.IndexMaxCharacters;
        EmbeddingBatchSize = source.EmbeddingBatchSize;
        EmbeddingChunkCharacters = source.EmbeddingChunkCharacters;
        EmbedHistory = source.EmbedHistory;
        AutoSummarize = source.AutoSummarize;
        SummaryMaxCalls = source.SummaryMaxCalls;
        GenerateUntilStop = source.GenerateUntilStop;
        MaxHistoryTurns = source.MaxHistoryTurns;
        ProtectedHistoryTurns = source.ProtectedHistoryTurns;
        ContextTokens = source.ContextTokens;
        MaxOutputTokens = source.MaxOutputTokens;
        SafetyTokens = source.SafetyTokens;
        TimeoutSeconds = source.TimeoutSeconds;
        Temperature = source.Temperature;
        Seed = source.Seed;
    }

    public void Validate()
    {
        if (Enrichment is null || ClassificationBudget is < 1 or > 2048 || NeutralCandidates is < 0 or > 64
            || !double.IsFinite(NeutralThreshold) || !double.IsFinite(RelevantThreshold) || NeutralThreshold < 0
            || RelevantThreshold <= NeutralThreshold || RelevantThreshold > 1 || QuerySummaryTokens is < 0 or > 4096
            || QuerySummaryMaxCalls is < 0 or > 8 || MemoryContextTokens < 0 || HistoryContextTokens < 0)
            throw new ArgumentException("Réglages de classification ou de contexte invalides.");
        Enrichment.Validate();
        if (AgentPlanMaxCalls is < 1 or > 64 || EmbeddingDocumentPrefix is null or { Length: > 256 } || EmbeddingQueryPrefix is null or { Length: > 256 } || MaxSearchQueries is < 1 or > 8 || RoutingMode is not ("off" or "auto" or "direct" or "memory" or "retrieval" or "explore") || RerankCandidateLimit is < 1 or > 64 || RerankTimeoutMs is < 50 or > 30000 || RetrievalMode is not ("legacy" or "lexical" or "hybrid") || EmbeddingModel is null || EmbeddingModel.Length > 200 || TokenCounting is not ("auto" or "conservative") || ToolMaxCalls is < 0 or > 4096 || SummaryMaxCalls is < 8 or > 2048 || MaxHistoryTurns is < 0 or > 100 || ContextTokens is < 1024 or > 131072
            || ToolContextTokens < 0 || ToolContextTokens >= ContextTokens || DocumentContextTokens < 0 || DocumentContextTokens >= ContextTokens
            || ToolResultBytes is < 1024 or > 131072 || IndexMaxCharacters < 0
            || ToolTimeoutSeconds is < 30 or > 86400
            || EmbeddingBatchSize is < 1 or > 64 || EmbeddingChunkCharacters is < 128 or > 3200
            || ProtectedHistoryTurns is < 0 or > 20
            || MaxOutputTokens < 1 || SafetyTokens < 128
            || (long)MaxOutputTokens + SafetyTokens >= ContextTokens
            || TimeoutSeconds is < 1 or > 600
            || !double.IsFinite(Temperature) || Temperature is < 0 or > 2)
        {
            throw new ArgumentException("Configuration Chat invalide : vérifiez fenêtre, contexte, réserve et délai.");
        }
    }
}
