using System.Text.Json.Serialization;

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
    [JsonIgnore] public RankingLimits Ranking => new(RerankEnabled, RerankCandidateLimit, RerankTimeoutMs);

    internal (int History, int Memory) LimitMemoryBudget(int available) =>
        (HistoryContextTokens > 0 ? Math.Min(available, HistoryContextTokens) : available,
         MemoryContextTokens > 0 ? Math.Min(available, MemoryContextTokens) : available);

    public ChatOptions Copy() => (ChatOptions)MemberwiseClone();
}

public readonly record struct RankingLimits(bool Enabled, int Candidates, int TimeoutMs);
