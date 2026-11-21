using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alycia.Client.Avalonia;

public sealed record ProjectToolsInfo(bool Enabled, bool ModelSupportsTools, int MaxCalls, string[] Tools, string ProtocolVersion);
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
public sealed partial class ChatApiClient
{
    public Task<ProjectToolsInfo> ProjectToolsAsync(Guid id, CancellationToken ct) => SendAsync<ProjectToolsInfo>(HttpMethod.Get, $"/conversations/{id}/tools", null, ct);
    public string McpEndpoint(Guid id)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/conversations/{id}/mcp", null);
        return request.RequestUri!.AbsoluteUri;
    }
}
public partial class ChatWindow
{
    private async Task ProjectToolsAsync()
    {
        if (_current is null) return;
        var info = await _client.ProjectToolsAsync(_current.Id, _lifetime.Token);
        var files = await _client.FilesAsync(_current.Id, _lifetime.Token);
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Label($"{files.Length} fichiers importés · {files.Count(f => SourceFileType.IsCode(f.Name))} fichiers de code · "
            + $"{files.Count(f => f.ContextMode == "excluded")} exclusions"));
        panel.Children.Add(Label(!info.Enabled ? "Outils désactivés. Activez-les dans Réglages pour permettre les lectures par l’IA."
            : !info.ModelSupportsTools ? "Ce modèle n’annonce pas la capacité tools. Les extraits restent disponibles ; choisissez un modèle compatible dans Réglages pour les lectures à la demande."
            : "Le modèle peut lister, rechercher et lire les sources de cette conversation. "
                + (info.MaxCalls == 0 ? "Sans quota d’appels ; budget du contexte et délai restent actifs." : $"Garde-fou : {info.MaxCalls} appels par demande.")));
        panel.Children.Add(Label("Les outils lisent les originaux textuels par portions. Leurs actions apparaissent pendant la réponse et sont conservées avec les échanges terminés. "
            + "Une recherche ciblée ne prouve pas une analyse complète : Analyse du projet parcourt toutes les sources admissibles et fournit leur couverture."));
        panel.Children.Add(Label("MCP local · Streamable HTTP · protocole " + info.ProtocolVersion));
        panel.Children.Add(new SelectableTextBlock { Text = _client.McpEndpoint(_current.Id), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Label("Cette URL expose les mêmes outils aux clients MCP compatibles, pour cette conversation. "
            + "En mode conteneur, utiliser le même jeton local que le client graphique."));
        var action = await ActionDialogAsync("Sources et outils", panel, [("Réglages", "settings"), ("Analyser tous les fichiers", "analysis")]);
        if (action == "settings") await SettingsAsync();
        if (action == "analysis") await AnalysesAsync();
    }
}
