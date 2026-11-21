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
        SourceCandidates = source.SourceCandidates?.Select(c => c with { References = [.. c.References] }).ToArray();
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

    public ProjectSearch ProjectSearch() => new(
        new RetrievalRequest(RetrievalMode, EligibleSourceIds?.ToFrozenSet(),
            PassageRankingSettings.Capture(this, PinnedSourceIds),
            QueryRewriteEnabled || FacetPlanningEnabled || QueryBriefModelEnabled, SearchPlan),
        EmbeddingQuery.Capture(this));

    public GenerationParameters GenerationParameters()
    {
        Validate();
        return new(ContextTokens, MaxOutputTokens, GenerateUntilStop, Temperature, Seed, TimeoutSeconds,
            CallPurpose, ResponseSchema?.Clone());
    }

    public ChatOptions Copy() => new(this);
}
