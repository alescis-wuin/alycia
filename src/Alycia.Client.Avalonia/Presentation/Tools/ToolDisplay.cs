using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

internal static class ToolDisplay
{
    public static string Describe(ToolObservation tool)
    {
        var label = tool.Name switch { "project_list" => "Inventaire", "project_search" => "Recherche", "project_read" => "Lecture", _ => tool.Name };
        var detail = tool.Evidence is { } file ? $"{file.Path}\nCaractères {file.Offset} → {file.End?.ToString() ?? file.NextOffset?.ToString() ?? "fin"} · "
            + (file.CompleteSource ? "texte source intégral accessible" : "extraction partielle") + $"\nSHA-256 : {file.Sha256}" : tool.Arguments;
        return $"{tool.Number}. {label} · {(tool.RepeatSuppressed ? "répétition évitée" : tool.Succeeded ? "réussi" : "refus ou erreur")}\n{detail}"
            + (tool.RetainedInFinalPrompt is bool retained ? (retained ? "\nRésultat conservé dans le dernier contexte." : "\nRésultat original retiré du dernier contexte.") : "")
            + (tool.ContextBudgetTokens > 0 ? $"\nContexte outils conservé : {tool.RetainedTokens}/{tool.ContextBudgetTokens} tokens estimés" : "")
            + (string.IsNullOrWhiteSpace(tool.Error) ? "" : "\nMotif : " + tool.Error);
    }
}
