using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alycia.Client.Avalonia;

internal sealed record ToolInspection(string Files, string Availability, string ProtocolVersion, string Endpoint)
{
    public static async Task<ToolInspection> LoadAsync(ChatApiClient client, Guid conversation, CancellationToken ct)
    {
        var info = await client.ProjectToolsAsync(conversation, ct);
        var files = await client.FilesAsync(conversation, ct);
        var counts = $"{files.Length} fichiers importés · {files.Count(f => SourceFileType.IsCode(f.Name))} fichiers de code · "
            + $"{files.Count(f => f.ContextMode == "excluded")} exclusions";
        var status = !info.Enabled ? "Outils désactivés. Activez-les dans Réglages pour permettre les lectures par l’IA."
            : !info.ModelSupportsTools ? "Ce modèle n’annonce pas la capacité tools. Les extraits restent disponibles ; choisissez un modèle compatible dans Réglages pour les lectures à la demande."
            : "Le modèle peut lister, rechercher et lire les sources de cette conversation. "
                + (info.MaxCalls == 0 ? "Sans quota d’appels ; budget du contexte et délai restent actifs." : $"Garde-fou : {info.MaxCalls} appels par demande.");
        return new(counts, status, info.ProtocolVersion, client.McpEndpoint(conversation));
    }

    public StackPanel CreatePanel(Func<string, TextBlock> label)
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(label(Files));
        panel.Children.Add(label(Availability));
        panel.Children.Add(label("Les outils lisent les originaux textuels par portions. Leurs actions apparaissent pendant la réponse et sont conservées avec les échanges terminés. "
            + "Une recherche ciblée ne prouve pas une analyse complète : Analyse du projet parcourt toutes les sources admissibles et fournit leur couverture."));
        panel.Children.Add(label("MCP local · Streamable HTTP · protocole " + ProtocolVersion));
        panel.Children.Add(new SelectableTextBlock { Text = Endpoint, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(label("Cette URL expose les mêmes outils aux clients MCP compatibles, pour cette conversation. "
            + "En mode conteneur, utiliser le même jeton local que le client graphique."));
        return panel;
    }
}
