using System.Collections.Immutable;
using Alycia.Contracts.Context;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Memory;
using System.Collections.Frozen;
using Alycia.Contracts.Retrieval;
using System.Text.Json.Serialization;
using Alycia.Contracts.Inference;

namespace Alycia.Modules.Chat.Application;

// Persisted settings are inherited; request-local state is never written to JSON.
public sealed class ChatOptions : ChatSettings
{
    [JsonIgnore] public System.Text.Json.JsonElement? ResponseSchema { get; set; }
    [JsonIgnore] public IReadOnlyList<SourceChunk>? SourceCandidates { get; set; }
    [JsonIgnore] public RetrievalReport? RetrievalReport { get; set; }
    [JsonIgnore] public QueryRoute? Route { get; set; }
    [JsonIgnore] public IReadOnlySet<Guid>? EligibleSourceIds { get; set; }
    [JsonIgnore] public TemporalRecall? Recall { get; set; }
    [JsonIgnore] public SearchQueryPlan? SearchPlan { get; set; }
    [JsonIgnore] public IReadOnlySet<Guid>? PinnedSourceIds { get; set; }
    [JsonIgnore] public ITextTokenCounter? TokenCounter { get; set; }
    [JsonIgnore] public string? ModelIdentity { get; set; }
    [JsonIgnore] public string CallPurpose { get; set; } = "chat";
    [JsonIgnore] public int AvailableInputTokens => ContextTokens - MaxOutputTokens - SafetyTokens;
    [JsonIgnore] public MemoryBudgetLimits MemoryBudget => new(HistoryContextTokens, MemoryContextTokens);

    public ChatOptions() { }

    private ChatOptions(ChatOptions source) : base(source)
    {
        ResponseSchema = source.ResponseSchema?.Clone();
        SourceCandidates = SearchSnapshots.CopyChunks(source.SourceCandidates);
        RetrievalReport = SearchSnapshots.Copy(source.RetrievalReport);
        Route = source.Route;
        EligibleSourceIds = source.EligibleSourceIds?.ToFrozenSet();
        Recall = source.Recall;
        SearchPlan = SearchSnapshots.Copy(source.SearchPlan);
        PinnedSourceIds = source.PinnedSourceIds?.ToFrozenSet();
        TokenCounter = source.TokenCounter;
        ModelIdentity = source.ModelIdentity;
        CallPurpose = source.CallPurpose;
    }

    public RoutingRequest RoutingRequest()
    {
        var plan = QueryRewriteEnabled || AgentPlanningEnabled || FacetPlanningEnabled || ClassificationEnabled || QueryBriefModelEnabled;
        var needsSources = RoutingMode != "off" || plan || TemporalMemoryEnabled || EvidenceSelectionEnabled || VerificationSearchEnabled;
        var scope = new PlannedQuery(UseProjectTools, Route, EligibleSourceIds?.ToFrozenSet(), PinnedSourceIds?.ToFrozenSet(), SearchPlan);
        var initial = new RoutingState(scope, Recall, SourceCandidates, RetrievalReport);
        return new(new(RoutingMode, 2 * (AutoSummarize ? ProtectedHistoryTurns : MaxHistoryTurns), MaxSearchQueries,
            plan, FacetPlanningEnabled || QueryBriefModelEnabled,
            QueryBriefModelEnabled && Enrichment.Model.Length > 0
                ? new BriefGeneration(Enrichment.Model, Enrichment.InputTokens, Enrichment.OutputTokens, Enrichment.TimeoutSeconds) : null), needsSources,
            TemporalMemoryEnabled ? TemporalRecallSettings.Capture(this) : null, ProjectSearch(), initial);
    }

    public ChatOptions WithRouting(RoutingState state)
    {
        var result = Copy();
        result.UseProjectTools = state.Query.UseTools;
        result.Route = state.Query.Route;
        result.EligibleSourceIds = state.Query.Eligible;
        result.PinnedSourceIds = state.Query.Pinned;
        result.Recall = state.Recall;
        result.SearchPlan = state.Query.Plan;
        result.SourceCandidates = state.Chunks;
        result.RetrievalReport = state.Report;
        return result;
    }

    public ProjectSearch ProjectSearch() => new(
        new RetrievalRequest(RetrievalMode, EligibleSourceIds?.ToFrozenSet(),
            PassageRankingSettings.Capture(this, PinnedSourceIds),
            QueryRewriteEnabled || FacetPlanningEnabled || QueryBriefModelEnabled, SearchPlan),
        EmbeddingQuery.Capture(this));

    public InferenceCall InferenceCall()
    {
        Validate();
        return Alycia.Contracts.Inference.InferenceCall.Capture(this, CallPurpose, ResponseSchema, TokenCounter);
    }

    public SummaryParameters Summary() => new(ContextTokens, SafetyTokens, SummaryMaxCalls, Enrichment.MaxDepth,
        Seed, TimeoutSeconds, TokenCounting, ModelIdentity, TokenCounter);

    public ClaimVerification Verification()
    {
        var enabled = VerificationSearchEnabled && RetrievalMode != "legacy" && Route?.Search != false;
        var query = enabled ? new ProjectSearch(new RetrievalRequest("lexical", EligibleSourceIds?.ToFrozenSet(),
            PassageRankingSettings.Capture(this, null) with { RerankEnabled = false },
            FacetPlanningEnabled || QueryBriefModelEnabled, null), EmbeddingQuery.Capture(this)) : null;
        return new(ClaimVerificationEnabled, Math.Min(3, TimeoutSeconds), query);
    }

    public ContextParameters Context()
    {
        var sources = new RoutingState(new PlannedQuery(UseProjectTools, Route, null, PinnedSourceIds?.ToFrozenSet(), null),
            Recall, SourceCandidates, RetrievalReport);
        return new(InferenceCall(), Summary(), ToolContextTokens,
            new(AutoSummarize, StructuredMemory, MaxHistoryTurns, ProtectedHistoryTurns, MemoryBudget),
            new(EvidenceSelectionEnabled, ClassificationEnabled, RelevantThreshold, RetrievalMode, DocumentContextTokens, sources),
            new(QuerySummaryTokens, QuerySummaryMaxCalls, Enrichment));
    }

    public ToolGeneration ToolGeneration()
    {
        var questions = SearchPlan?.Brief?.Facets.Select(f => f.Question)
            ?? SearchPlan?.Queries.Skip(1).Select(q => q.Text) ?? [];
        return new(InferenceCall(), Summary(), EvidenceSelectionEnabled, AgentPlanningEnabled, Route?.Mode == "memory",
            AgentPlanMaxCalls, MaxSearchQueries, ToolMaxCalls, ToolContextTokens, ToolResultBytes, ToolTimeoutSeconds,
            questions.ToImmutableArray(), SearchPlan?.Original);
    }

    public ChatOptions Copy() => new(this);
}
