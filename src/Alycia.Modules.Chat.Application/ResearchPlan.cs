using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public sealed class ResearchPlan
{
    private readonly List<ResearchGoal> _goals;
    private readonly int _limit;
    private readonly bool _history;
    private int _current = -1;
    private (int Goal, ModelToolCall Call)? _followup;
    private bool _readingOriginal;
    private readonly HashSet<int> _paged = [];
    public ResearchPlan(ChatOptions options, ContextWindow context)
    {
        _limit = options.ToolMaxCalls > 0 ? Math.Min(options.ToolMaxCalls, options.AgentPlanMaxCalls) : options.AgentPlanMaxCalls;
        _history = options.Route?.Mode == "memory";
        var queries = options.SearchPlan?.Queries ?? [];
        var targeted = options.SearchPlan?.Brief?.Facets.Select(f => f.Question).ToArray() ?? queries.Skip(1).Select(q => q.Text).ToArray();
        if (targeted.Length == 0) targeted = [options.SearchPlan?.Original ?? context.Messages.LastOrDefault()?.Content ?? ""];
        _goals = targeted.Where(q => !string.IsNullOrWhiteSpace(q)).Take(options.MaxSearchQueries).Select(q =>
        {
            var supplied = (context.Sources ?? []).Where(s => s.Reference is not null && s.SourceType != "summary" &&
                (s.Path.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Content?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                .Select(s => s.Reference!).Distinct().ToArray();
            return new ResearchGoal(q, supplied.Length > 0 ? "supplied" : "not_searched", supplied);
        }).ToList();
    }
    public bool Exhausted(int calls) => calls >= _limit;
    public ModelToolCall? Next(int calls)
    {
        if (Exhausted(calls)) return null;
        if (_followup is { } followup)
        {
            _current = followup.Goal; _followup = null;
            _readingOriginal = followup.Call.Function.Name == "project_read";
            return followup.Call;
        }
        _readingOriginal = false;
        _current = _goals.FindIndex(g => g.State == "not_searched");
        if (_current < 0) return null;
        var q = FileContext.Utf8Prefix(_goals[_current].Query, 120).Trim();
        if (q.Length < 2) { _goals[_current] = _goals[_current] with { State = "not_searchable" }; return null; }
        return new(new(_history ? "history_search" : "project_search_passages",
            JsonSerializer.SerializeToElement(new { query = q, limit = 3 })));
    }
    public void Observe(ProjectToolResult result)
    {
        if (_current < 0) return;
        var original = (result.EvidenceItems ?? []).Concat(result.Evidence is null ? [] : [result.Evidence])
            .Where(e => e.Reference is not null).Select(e => e.Reference!).Concat((result.History ?? []).Select(h => h.Reference)).Distinct().ToArray();
        var more = false; int? nextOffset = null, nextBefore = null;
        try
        {
            using var document = JsonDocument.Parse(result.Text);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("nextOffset", out var offset) && offset.ValueKind == JsonValueKind.Number && offset.TryGetInt32(out var n)) nextOffset = n;
                if (document.RootElement.TryGetProperty("nextBefore", out var before) && before.ValueKind == JsonValueKind.Number && before.TryGetInt32(out var b)) nextBefore = b;
            }
            more = new[] { "nextOffset", "nextBefore" }.Any(key => document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number);
        }
        catch (JsonException) { }
        var previous = _goals[_current];
        var state = result.IsError ? "inaccessible" : original.Length > 0 ? "passages_read" : more ? "partial" : "not_found";
        var partial = (result.EvidenceItems ?? []).FirstOrDefault(e => e.FileId.HasValue && e.NextOffset.HasValue);
        if (!result.IsError && !_readingOriginal && partial is not null)
        {
            _followup = (_current, new(new("project_read", JsonSerializer.SerializeToElement(new { fileId = partial.FileId, offset = partial.Offset }))));
            state = "partial";
        }
        else if (!result.IsError && original.Length == 0 && more && _paged.Add(_current))
        {
            var args = new Dictionary<string, object> { ["query"] = FileContext.Utf8Prefix(previous.Query, 120), ["limit"] = 3 };
            if (nextOffset.HasValue) args["offset"] = nextOffset.Value;
            if (nextBefore.HasValue) args["before"] = nextBefore.Value;
            _followup = (_current, new(new(_history ? "history_search" : "project_search_passages", JsonSerializer.SerializeToElement(args))));
        }
        if (result.Evidence?.NextOffset.HasValue == true) state = "partial";
        _goals[_current] = previous with { State = state, References = previous.References.Concat(original).Distinct().ToArray() };
        _current = -1;
    }
    public void ObserveModelRead(ModelToolCall call, ProjectToolResult result)
    {
        var paths = (result.EvidenceItems ?? []).Concat(result.Evidence is null ? [] : [result.Evidence]).Select(e => e.Path).ToArray();
        var query = call.Function.Arguments.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null;
        _current = _goals.FindIndex(g => g.State != "supplied" &&
            (string.Equals(query, g.Query, StringComparison.OrdinalIgnoreCase) || paths.Any(p => p.Contains(g.Query, StringComparison.OrdinalIgnoreCase))));
        if (_current >= 0) { _readingOriginal = true; Observe(result); }
    }
    public ResearchReport Report(string reason, int calls) => new(reason, _limit, calls, _goals.ToArray());
}
