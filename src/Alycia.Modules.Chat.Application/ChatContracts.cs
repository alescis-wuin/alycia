using Alycia.Modules.Chat.Domain;
using System.Text.Json.Serialization;

namespace Alycia.Modules.Chat.Application;

public interface IChatTurnScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public interface IChatRepository
{
    Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct) => Task.FromResult<IChatTurnScope?>(null);
    Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken cancellationToken);
    async Task<IReadOnlyList<Attachment>> ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        (await FilesAsync(id, ct)).Where(f => f.ContextMode != "excluded"
            && ((f.ContextMode == "pinned" || f.ContextMode == "project") || f.AnchorSequence >= first)).ToArray();
    async Task<IReadOnlyList<ChatMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
        (await MessagesAsync(id, through - after, through + 1, ct)).Where(m => m.Sequence > after && m.Sequence <= through).ToArray();
    Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken);
    Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken cancellationToken);
    Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant, CancellationToken cancellationToken);
}

public sealed record PromptMessage(string Role, string Content,
    [property: JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ModelToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Thinking = null);
public sealed record ContextWindow(IReadOnlyList<PromptMessage> Messages, int HistoryMessagesIncluded, int EstimatedInputTokens, int FilesIncluded = 0, bool NeedsSummary = false, int SummaryThroughSequence = 0, string SummaryText = "", int SummaryCalls = 0, IReadOnlyList<SourceExcerpt>? Sources = null, int FilesAvailable = 0, int ToolReserve = 0, RetrievalReport? Retrieval = null, MemoryReport? Memory = null, QueryRoute? Route = null, EvidenceAssembly? Assembly = null, TemporalReport? Temporal = null,
    [property: JsonIgnore] IReadOnlyList<EvidenceText>? MemoryTexts = null, UsedGuide[]? Guides = null);
public sealed record GenerationStats(int? PromptTokens, int? OutputTokens, long? TotalNanoseconds,
    long? EvaluationNanoseconds, string? DoneReason, long? PromptEvaluationNanoseconds = null, long? LoadNanoseconds = null, int? CachedPromptTokens = null);
public sealed record GenerationReply(string Content, GenerationStats? Stats = null, IReadOnlyList<ModelToolCall>? ToolCalls = null, string? Thinking = null, IReadOnlyList<ToolObservation>? Tools = null, IReadOnlyList<string>? Warnings = null,
    [property: JsonIgnore] IReadOnlyList<EvidenceText>? EvidenceTexts = null, ResearchReport? Research = null);
public sealed record ModelDelta(string Content, string? Thinking = null, string? Status = null, ToolObservation? Tool = null);
public sealed record SendMessageResult(IReadOnlyList<ChatMessage> Messages, int? HistoryMessagesIncluded,
    bool Replayed, int FilesIncluded = 0, int? EstimatedInputTokens = null, GenerationStats? Stats = null, GenerationUsage? Usage = null);

public interface IChatModel
{
    Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions options, CancellationToken ct) => Task.FromResult(options.Copy());
    async Task<GenerationReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct) => new(await GenerateAsync(model, messages, options, ct));
    Task<long?> ResidentVramAsync(string model, CancellationToken ct) => Task.FromResult<long?>(null);
    Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => Task.FromResult(false);
    Task<GenerationReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        throw new ChatModelException("Ce fournisseur ne prend pas en charge les outils.");
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken cancellationToken);
    // Providers enforce settings.TimeoutSeconds after acquiring their inference slot; ct cancels queueing too.
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings, CancellationToken ct)
        => GenerateAsync(model, messages, ct);
    async Task<GenerationReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken ct)
    {
        var content = await GenerateAsync(model, messages, settings, ct);
        await onDelta(new(content));
        return new(content);
    }
}

public sealed class ChatConflictException() : Exception("La conversation a changé. Rechargez-la puis réessayez.");
public sealed class ConversationNotFoundException() : Exception("Conversation introuvable.");
public enum ChatModelFailure { Other, EmptyContent, Timeout }
public sealed class ChatModelException(string message, GenerationStats? stats = null, ChatModelFailure failure = ChatModelFailure.Other) : Exception(message)
{
    public GenerationStats? Stats { get; } = stats;
    public ChatModelFailure Failure { get; } = failure;
}

public sealed class ChatOptions
{
    public bool StructuredMemory { get; set; } = true;
    [JsonIgnore] public System.Text.Json.JsonElement? ResponseSchema { get; set; }
    public string RetrievalMode { get; set; } = "lexical";
    public string EmbeddingModel { get; set; } = "";
    [JsonIgnore] public IReadOnlyList<SourceChunk>? SourceCandidates { get; set; }
    [JsonIgnore] public RetrievalReport? RetrievalReport { get; set; }
    public string RoutingMode { get; set; } = "off";
    [JsonIgnore] public QueryRoute? Route { get; set; }
    [JsonIgnore] public IReadOnlySet<Guid>? EligibleSourceIds { get; set; }
    public bool TemporalMemoryEnabled { get; set; }
    [JsonIgnore] public TemporalRecall? Recall { get; set; }
    public bool AdaptiveInferenceEnabled { get; set; }
    public bool ClaimVerificationEnabled { get; set; }
    public bool VerificationSearchEnabled { get; set; }
    public int AgentPlanMaxCalls { get; set; } = 8;
    public bool AgentPlanningEnabled { get; set; }
    public bool QueryRewriteEnabled { get; set; }
    public int MaxSearchQueries { get; set; } = 3;
    [JsonIgnore] public SearchQueryPlan? SearchPlan { get; set; }
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
    [JsonIgnore] public IReadOnlySet<Guid>? PinnedSourceIds { get; set; }
    public bool RerankEnabled { get; set; }
    public int RerankCandidateLimit { get; set; } = 24;
    public int RerankTimeoutMs { get; set; } = 1500;
    public string TokenCounting { get; set; } = "auto";
    [JsonIgnore] public ITextTokenCounter? TokenCounter { get; set; }
    [JsonIgnore] public string? ModelIdentity { get; set; }
    [JsonIgnore] public string CallPurpose { get; set; } = "chat";
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
    public ChatOptions Copy() => (ChatOptions)MemberwiseClone();
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
