using System.Text.RegularExpressions;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class QueryRouting
{
    private static bool Match(string text, string pattern) => Regex.IsMatch(text, pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    public static QueryRoute Decide(string question, ChatOptions options, IReadOnlyList<Attachment> files)
    {
        var mode = options.RoutingMode;
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
        return new(mode, reason, mode is "explore" or "retrieval", options.UseProjectTools && mode is "explore" or "retrieval" or "memory");
    }

    public static async Task PrepareAsync(IChatRepository repository, Conversation conversation, string question,
        ChatOptions options, IProjectRetrieval? retrieval, MeteredChatModel measured, CancellationToken ct, ITemporalMemory? memory = null)
    {
        ct.ThrowIfCancellationRequested();
        if (retrieval is null && options.RoutingMode == "off" && !options.QueryRewriteEnabled && !options.AgentPlanningEnabled &&
            !options.TemporalMemoryEnabled && !options.EvidenceSelectionEnabled && !options.VerificationSearchEnabled && !options.FacetPlanningEnabled && !options.ClassificationEnabled && !options.QueryBriefModelEnabled) return;
        var first = Math.Max(1, conversation.MessageCount - (options.AutoSummarize ? options.ProtectedHistoryTurns : options.MaxHistoryTurns) * 2 + 1);
        var files = await repository.ContextFilesAsync(conversation.Id, first, ct);
        options.EligibleSourceIds = files.Where(f => ProjectContext.Eligible(f, first, conversation.MessageCount + 1)).Select(f => f.Id).ToHashSet();
        options.PinnedSourceIds = files.Where(f => f.ContextMode == "pinned" && options.EligibleSourceIds.Contains(f.Id)).Select(f => f.Id).ToHashSet();
        try
        {
            if (options.QueryRewriteEnabled || options.AgentPlanningEnabled || options.FacetPlanningEnabled || options.ClassificationEnabled || options.QueryBriefModelEnabled)
            {
                var previous = await repository.MessagesAsync(conversation.Id, 12, null, ct);
                options.SearchPlan = QueryPlanning.Build(question, previous.Where(m => m.ConversationId == conversation.Id).ToArray(),
                    files.Where(f => options.EligibleSourceIds.Contains(f.Id)).ToArray(), options.MaxSearchQueries, options.FacetPlanningEnabled || options.QueryBriefModelEnabled);
                if (options.SearchPlan is { Brief: { } brief } plan)
                {
                    if (options.QueryBriefModelEnabled && plan.Ambiguous && options.Enrichment.Model.Length > 0)
                        brief = await QueryPlanning.RefineAsync(brief, options.Enrichment, measured, ct);
                    var decision = Decide(question, options, files.Where(f => options.EligibleSourceIds.Contains(f.Id)).ToArray());
                    string[] categories = decision.Mode switch
                    {
                        "memory" => ["messages", "memory"],
                        "explore" => ["documents", "tools"],
                        "direct" or "unavailable" => [],
                        _ => ["documents"]
                    };
                    options.SearchPlan = plan with
                    {
                        Brief = brief with { Categories = categories },
                        Queries = brief.Method == "rules-v1" ? plan.Queries : plan.Queries.Append(new SearchTerm(brief.Summary, "lexical")).ToArray()
                    };
                }
            }
            if (options.RoutingMode != "off")
            {
                var route = Decide(question, options, files.Where(f => options.EligibleSourceIds.Contains(f.Id)).ToArray());
                if (options.SearchPlan is { Resolved.Length: > 0 } && !route.Search && route.Mode != "unavailable")
                    route = new("retrieval", "référence au tour précédent", true, options.UseProjectTools);
                options.Route = route;
                options.UseProjectTools &= route.Tools;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            options.SearchPlan = null;
            options.Route = new("fallback", "délai des règles dépassé", true, options.UseProjectTools);
        }
        if (options.TemporalMemoryEnabled && memory is not null && options.Route?.Mode is not ("direct" or "unavailable"))
        {
            options.Recall = await memory.RecallAsync(conversation.Id, question, options, conversation.MessageCount, ct);
            measured.AddCalls(options.Recall.Calls);
        }
        if (options.Route?.Search == false) { options.SourceCandidates = []; return; }
        if (retrieval is null || options.RetrievalMode == "legacy") return;
        var sources = await retrieval.PrepareAsync(conversation.Id, question, options, ct);
        options.SourceCandidates = sources.Chunks;
        options.RetrievalReport = sources.Report;
        measured.AddCalls(sources.Calls);
    }
}
