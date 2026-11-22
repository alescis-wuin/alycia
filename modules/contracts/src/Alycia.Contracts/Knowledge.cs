using System.Text.Json;

namespace Alycia.Contracts.Chat;

public sealed record EnrichmentOptions(bool Enabled = false, string Model = "", int JobCalls = 64,
    int CallsPerUnit = 2, int InputTokens = 4096, int OutputTokens = 768, int TargetTokens = 256,
    int Workers = 2, int TimeoutSeconds = 120, int MaxDepth = 12)
{
    public void Validate()
    {
        if (Model is null or { Length: > 200 } || JobCalls is < 1 or > 8192 || CallsPerUnit is < 1 or > 4
            || InputTokens is < 2048 or > 32768 || OutputTokens is < 128 or > 4096 || TargetTokens is < 64 or > 1024
            || OutputTokens + 512 >= InputTokens || TargetTokens > OutputTokens || Workers is < 1 or > 4 || TimeoutSeconds is < 5 or > 600 || MaxDepth is < 1 or > 24)
            throw new ArgumentException("Vérifiez la fenêtre, les réserves, les appels et la concurrence de l’indexation.");
    }
    // Scheduling changes do not invalidate a content artefact.
    [System.Text.Json.Serialization.JsonIgnore] public string Profile => MemoryTransfer.Hash(JsonSerializer.Serialize(new { Version = "knowledge-v1", Model, InputTokens, OutputTokens, TargetTokens }));
}
public sealed record UnitLabels(string Title, string Description, string[] Keywords, string[] Topics, string[] Entities,
    string DocumentType, string[] TechnicalSymbols, string Language, string Summary, string[] Facts, string[] DateMentions,
    string[] Uncertainties, string[] SourceUnitIds)
{
    public string Terms() => string.Join(' ', Keywords.Concat(Topics).Concat(Entities).Concat(TechnicalSymbols));
}
public sealed record IndexedNote(string Key, string Level, string Profile, string Identity, string ContentHash,
    UnitLabels Labels, string State, string[] Children, string Role, string AssertionType, int First, int Last,
    DateTimeOffset? Observed, DateTimeOffset IndexedAt, int Calls, int InputTokens, bool Complete,
    string? SourceCreated = null, string? SourceModified = null, DateTimeOffset? Uploaded = null, bool HasFallback = false, string? InputDigest = null,
    string? Path = null, string? SourceDateOrigin = null, string PromptVersion = "annotation-fr-v1",
    int TargetTokens = 0, int ReservedOutputTokens = 0);
public sealed record EmbeddingProgress(Guid OwnerId, bool IsProject, string State, int Completed, int Total,
    string Detail, string Model, DateTimeOffset UpdatedAt, int BatchSize = 16, int ChunkCharacters = 1600,
    bool Hierarchical = false, string DocumentPrefix = "", string QueryPrefix = "", EnrichmentOptions? Enrichment = null, int CycleCalls = 0);
