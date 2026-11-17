using System;
using System.Linq;
using System.Text;
using Alycia.Modules.Chat.Application;

namespace Alycia.Client.Avalonia;

internal static class ResponseDiagnostics
{
    public static string Title(TurnDiagnostics? report) => report?.Warnings is { Count: > 0 }
        ? "⚠ Contexte partiellement conservé — détails" : report?.Citations?.Status switch
        {
            "unknown_references" => "⚠ Références inconnues — examiner les sources",
            "sources_uncited" => "Sources fournies, aucune référence citée",
            "references_recognized" => "Sources citées et coût de la réponse",
            _ => "Contexte et coût de la réponse"
        };
    public static string Describe(TurnDiagnostics? report)
    {
        if (report is null) return "Diagnostics illisibles.";
        var text = new StringBuilder();
        foreach (var warning in report.Warnings ?? []) text.AppendLine("⚠ " + warning);
        if (report.Usage is { } usage)
        {
            text.AppendLine($"{usage.Calls.Count} appels · {usage.InputTokens?.ToString() ?? "?"} tokens entrants cumulés · {usage.OutputTokens?.ToString() ?? "?"} sortants cumulés");
            text.AppendLine($"Durée des appels : {usage.ElapsedMilliseconds / 1000:F1} s · {(usage.Complete ? "comptes fournis par Ollama" : "mesures incomplètes du fournisseur")}");
            text.AppendLine("Comptage préparatoire : " + usage.Counter);
            text.AppendLine("Identité du modèle : " + (usage.ModelIdentity ?? "non disponible"));
            foreach (var call in usage.Calls.Where(c => c.FirstContentMilliseconds is not null))
                text.AppendLine($"Premier fragment de réponse ({call.Purpose}) : {call.FirstContentMilliseconds / 1000:F2} s après le début de cet appel.");
            foreach (var group in usage.Calls.GroupBy(c => c.Purpose))
                text.AppendLine($"  {group.Key} : {group.Count()} appel(s), {group.Sum(c => c.ElapsedMilliseconds) / 1000:F1} s");
        }
        if (report.Retrieval is { } retrieval)
        {
            text.AppendLine($"\nRecherche {retrieval.Mode} : {retrieval.IndexedFiles}/{retrieval.EligibleFiles} fichiers indexés, {retrieval.CandidateChunks} passages candidats.");
            text.AppendLine($"Index : {retrieval.IndexMilliseconds:F1} ms · classement : {retrieval.RankingMilliseconds:F1} ms · {retrieval.ZeroScoreChunks} scores lexicaux nuls.");
            if (retrieval.Cache is { } cache)
                text.AppendLine($"Cache lexical : {(cache.LexicalHit ? "réutilisé" : "construit")} · vecteurs : {cache.VectorHits} réutilisés, {cache.VectorMisses} calculés · {(cache.VectorWritten ? "cache écrit" : "aucune écriture vectorielle")}.");
            foreach (var group in (retrieval.Decisions ?? []).GroupBy(d => d.Reason))
                text.AppendLine($"  {group.Key} : {group.Count()} passage(s)");
            if (retrieval.UnreportedDecisions > 0) text.AppendLine($"{retrieval.UnreportedDecisions} décisions supplémentaires non détaillées.");
            foreach (var omission in retrieval.Omissions) text.AppendLine(omission);
        }
        if (report.Memory is { } memory)
            text.AppendLine($"Mémoire : {memory.Selected}/{memory.Stored} citations sélectionnées, {memory.Omitted} entrées non incluses ou écartées, {memory.FallbackSegments} fragments de repli.");
        if (report.Citations is { } citations)
        {
            text.AppendLine($"\n{citations.Recognized.Length} référence(s) reconnue(s) · {citations.Unknown.Length} inconnue(s).");
            text.AppendLine($"Couverture fournie : {citations.Supplied.Select(s => s.FileId).Distinct().Count()} source(s), {citations.Supplied.Length} passage(s). Compréhension et exhaustivité non établies.");
            text.AppendLine("Ce contrôle relie les identifiants aux passages fournis. Il ne vérifie pas la vérité des affirmations ni la couverture de tout le projet.");
            if (citations.Unknown.Length > 0) text.AppendLine("Inconnues : " + string.Join(", ", citations.Unknown));
            foreach (var source in citations.Supplied)
                text.AppendLine($"\n[{source.Reference}] {source.Path}:L{source.StartLine}–L{source.EndLine} · {(citations.Recognized.Contains(source.Reference) ? "cité" : "fourni, non cité")}"
                    + $"\nCaractères UTF-16 [{source.Start}, {source.End}[ · fichier {source.FileId}\nSHA-256 : {source.Sha256}");
            foreach (var source in citations.Historical ?? [])
                text.AppendLine($"\n[{source.Reference}] message {source.Sequence} · {source.Role} · {source.CreatedAtUtc:O} · {(source.Partial ? "extrait" : "message complet")}\nSHA-256 : {source.Sha256}");
        }
        if (report.Claims is { } claims)
        {
            text.AppendLine("\nAffirmations — contrôle heuristique non bloquant, pas une validation de vérité.");
            foreach (var finding in claims.Findings)
                text.AppendLine($"{finding.Status} · {finding.Text}\n{finding.Reason}");
            if (claims.Omitted > 0) text.AppendLine($"{claims.Omitted} blocs non évalués.");
        }
        return text.ToString().Trim();
    }
}
