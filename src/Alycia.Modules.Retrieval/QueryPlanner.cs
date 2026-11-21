using System.Collections.Frozen;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Documents;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Retrieval;

public sealed class QueryPlanner(IQuerySources repository) : IQueryPlanner
{
    private static bool Match(string text, string pattern) => Regex.IsMatch(text, pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    public static QueryRoute Decide(string question, string routingMode, bool useTools, IReadOnlyList<TurnFile> files)
    {
        var mode = routingMode;
        var usable = files.Where(f => f.ContextMode != "excluded").ToArray();
        var pinned = usable.Any(f => f.ContextMode == "pinned");
        var explicitFile = usable.Any(f => question.Contains(Path.GetFileName(f.Name), StringComparison.OrdinalIgnoreCase));
        var explore = Match(question, @"\b(compare\w*|comparaisons?|explor\w*|exhaust\w*|multi.fichiers?|audit\w*)\b");
        var document = explicitFile || Match(question, @"\b(document\w*|fichier\w*|files?|source\w*|code|méthode|method|symbole?|class|projet|project|recherch\w*|search|find|lis|read)\b|\.[A-Za-z]{2,5}\b");
        var history = Match(question, @"\b(histor\w*|décision\w*|decision\w*|souviens|remember|précéd\w*|previous\w*|avions|auparavant|avant|earlier)\b");
        if (mode == "auto" && history && usable.Length > 0 && !Match(question, @"\b(souviens|avions|avons|remember|nous|conversation|message\w*|décision\w*|decision\w*)\b")
            && Match(question, @"\b\d{4}(?:-\d{2}){0,2}\b")) document = true;
        var direct = Match(question.Trim(), @"^(bonjour|bonsoir|merci|salut|hello|hi|thanks|thank you|coucou)[\s!?.]*$") ||
            Match(question.Trim(), @"^(?:calcule\s+|what is\s+|combien (?:font|fait)\s+)?[\d\s+*/().,=?-]+$");
        var namedRead = Match(question, @"\b(ouvr\w*|lis|lire|affich\w*|open|read|show)\b") &&
            Match(question, @"[\w/-]+\.(?:md|txt|cs|py|json|csv|pdf|docx|xlsx|pptx|yaml|yml|sql|ts|js|rs|go|xml|axaml)\b");
        if (namedRead && !pinned && !explicitFile && !explore)
            return new("unavailable", "aucun fichier accessible correspondant à la lecture demandée", false, false);
        var reason = mode == "auto" ? "règles de la demande" : "choix de la conversation";
        if (pinned || document || explore)
        {
            reason = pinned ? "source épinglée" : "source ou exploration demandée";
            mode = explore || mode == "explore" ? "explore" : "retrieval";
        }
        else if (mode == "auto")
            mode = history ? "memory" : direct || usable.Length == 0 ? "direct" : "retrieval";
        return new(mode, reason, mode is "explore" or "retrieval", useTools && mode is "explore" or "retrieval" or "memory");
    }

    public async Task<PlannedQuery> PlanAsync(Guid owner, int messageCount, string question, RoutingRules request,
        PlannedQuery prior, IQueryBriefRefiner refiner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var first = Math.Max(1, messageCount - request.HistoryMessages + 1);
        var files = await repository.ContextFilesAsync(owner, first, ct);
        var visible = files.Where(f => f.InContextWindow(first, messageCount + 1)).ToArray();
        var eligible = visible.Select(f => f.Id).ToFrozenSet();
        var pinned = visible.Where(f => f.ContextMode == "pinned").Select(f => f.Id).ToFrozenSet();
        var plan = prior.Plan;
        var route = prior.Route;
        var useTools = prior.UseTools;
        try
        {
            if (request.PlanQueries)
            {
                var previous = await repository.RecentForQueryAsync(owner, 12, ct);
                plan = QueryPlanning.Build(question, previous.Where(m => m.ConversationId == owner).ToArray(),
                    visible, request.MaxSearchQueries, request.Facets);
                if (plan.Brief is { } brief)
                {
                    if (request.Refiner is { } parameters && plan.Ambiguous)
                        brief = await refiner.RefineAsync(brief, parameters, ct);
                    var decision = Decide(question, request.Mode, useTools, visible);
                    string[] categories = decision.Mode switch
                    {
                        "memory" => ["messages", "memory"],
                        "explore" => ["documents", "tools"],
                        "direct" or "unavailable" => [],
                        _ => ["documents"]
                    };
                    plan = plan with
                    {
                        Brief = brief with { Categories = categories },
                        Queries = brief.Method == "rules-v1" ? plan.Queries : plan.Queries.Append(new SearchTerm(brief.Summary, "lexical")).ToArray()
                    };
                }
            }
            if (request.Mode != "off")
            {
                route = Decide(question, request.Mode, useTools, visible);
                if (plan is { Resolved.Length: > 0 } && !route.Search && route.Mode != "unavailable")
                    route = new("retrieval", "référence au tour précédent", true, useTools);
                useTools &= route.Tools;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            plan = null;
            route = new("fallback", "délai des règles dépassé", true, useTools);
        }

        return new(useTools, route, eligible, pinned, plan);
    }
}
