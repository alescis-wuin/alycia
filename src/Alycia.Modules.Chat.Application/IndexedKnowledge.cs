using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Chat.Application;

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
    [System.Text.Json.Serialization.JsonIgnore] public string Profile => StructuredMemory.Hash(JsonSerializer.Serialize(new { Version = "knowledge-v1", Model, InputTokens, OutputTokens, TargetTokens }));
}

public sealed record UnitLabels(string Title, string Description, string[] Keywords, string[] Topics, string[] Entities,
    string DocumentType, string[] TechnicalSymbols, string Language, string Summary, string[] Facts, string[] DateMentions,
    string[] Uncertainties, string[] SourceUnitIds);

public sealed record IndexedNote(string Key, string Level, string Profile, string Identity, string ContentHash,
    UnitLabels Labels, string State, string[] Children, string Role, string AssertionType, int First, int Last,
    DateTimeOffset? Observed, DateTimeOffset IndexedAt, int Calls, int InputTokens, bool Complete,
    string? SourceCreated = null, string? SourceModified = null, DateTimeOffset? Uploaded = null, bool HasFallback = false, string? InputDigest = null,
    string? Path = null, string? SourceDateOrigin = null, string PromptVersion = "annotation-fr-v1",
    int TargetTokens = 0, int ReservedOutputTokens = 0);

public sealed record PassageGuide(string Reference, string Summary, string Terms, string Identity, string State,
    string? Created, string? Modified, DateTimeOffset? Uploaded, DateTimeOffset IndexedAt, string NodeKey, string Level, bool Partial = false);

// Atomic reservations precede model calls. Exhaustion yields to the next worker pass.
public sealed class IndexCallBudget(int maximum)
{
    private readonly int _capacity = maximum;
    private int _remaining = maximum;
    public int Remaining => Volatile.Read(ref _remaining);
    public int Used => _capacity - Remaining;
    public bool TryReserve()
    {
        int remaining;
        do { remaining = Volatile.Read(ref _remaining); if (remaining == 0) return false; }
        while (Interlocked.CompareExchange(ref _remaining, remaining - 1, remaining) != remaining);
        return true;
    }
}

public static class IndexedKnowledge
{
    public const string Extractive = "extractive-v1";
    public static string Key(params string[] parts) => StructuredMemory.Hash(JsonSerializer.Serialize(parts));
    public static string Terms(UnitLabels a) => string.Join(' ', a.Keywords.Concat(a.Topics).Concat(a.Entities).Concat(a.TechnicalSymbols));
    public static string Fit(string text, int budget, ChatOptions settings)
    {
        if (TokenBudget.Count(text, settings) <= budget) return text;
        var low = 0; var high = text.Length;
        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (TokenBudget.Count(text[..mid], settings) <= budget) low = mid; else high = mid - 1;
        }
        if (low > 0 && low < text.Length && char.IsLowSurrogate(text[low])) low--;
        return text[..low];
    }

    public static UnitLabels Extract(string title, string text, string type, string[] ids, int target, ChatOptions counter)
    {
        var keywords = ProjectRetrieval.Tokens(text).GroupBy(t => t).OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal).Take(12).Select(g => g.Key).ToArray();
        var summary = Fit(text.Trim(), target, counter);
        var dates = Regex.Matches(text, @"\b\d{4}-\d{2}-\d{2}\b").Select(m => m.Value).Distinct().Take(12).ToArray();
        return new(FileContext.Utf8Prefix(title, 200), "", keywords, [], [], type, [], "und", summary, [], dates, [], ids);
    }

    private static readonly string[] Strings = ["title", "description", "documentType", "language", "summary"];
    private static readonly string[] Lists = ["keywords", "topics", "entities", "technicalSymbols", "facts", "dateMentions", "uncertainties", "sourceUnitIds"];
    public static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = Strings.Concat(Lists),
        properties = Strings.ToDictionary(k => k, _ => (object)new { type = "string", maxLength = 2048 })
            .Concat(Lists.ToDictionary(k => k, _ => (object)new { type = "array", maxItems = 32, items = new { type = "string", maxLength = 256 } }))
            .ToDictionary(p => p.Key, p => p.Value)
    });

    public static UnitLabels? Validate(string json, string[] expectedIds, int target, ChatOptions counter)
    {
        if (json.Length > 32000) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != Strings.Length + Lists.Length
                || Strings.Any(k => !root.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.String || v.GetString()!.Length > 2048)
                || Lists.Any(k => !root.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() > 32
                    || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || x.GetString()!.Length > 256))) return null;
            var labels = root.Deserialize<UnitLabels>(ProjectToolCatalog.Json)!;
            return labels.SourceUnitIds.SequenceEqual(expectedIds) && !string.IsNullOrWhiteSpace(labels.Summary)
                && TokenBudget.Count(labels.Summary, counter) <= target ? labels : null;
        }
        catch (JsonException) { return null; }
    }
}
