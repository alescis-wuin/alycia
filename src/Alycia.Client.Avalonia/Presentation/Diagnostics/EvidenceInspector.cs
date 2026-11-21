using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

internal sealed class EvidenceInspector(EvidenceSelection evidence, ChatApiClient client, CancellationToken ct)
{
    private readonly MarkdownView _passage = new() { IsMarkdown = false };

    public Task OpenCitationAsync(CitationEventArgs citation)
    {
        var source = evidence.Resolve(citation.Message, citation.Source.Reference);
        return source is null ? Task.CompletedTask : ReadPassageAsync(source);
    }

    private async Task ReadPassageAsync(SuppliedSource source)
    {
        try
        {
            var version = await client.SourceAsync(evidence.Conversation, false, source.FileId, ct, source.Sha256, source.Start, source.End);
            var truncated = version.End < source.End ? "\nExtrait affiché partiellement." : "";
            _passage.Text = $"{version.Name}\nSHA-256 {version.Sha256}{truncated}\n\n{version.Text}";
            _passage.BringIntoView();
        }
        catch (ChatApiException e) { _passage.Text = "Passage indisponible. " + e.Message; }
    }

    public void Render(StackPanel body, Func<string, TextBlock> label, Func<string, Func<Task>, Button> actionButton,
        Func<MarkdownView, Guid, int, string, Task> readHistory)
    {
        var conversation = evidence.Conversation;
        var message = evidence.Message;
        var diagnostics = evidence.Diagnostics;
        if (diagnostics?.WorkspaceRevision is int revision) body.Children.Add(label($"Projet au moment de la réponse · révision {revision}"));
        var passages = diagnostics?.Citations?.Supplied ?? [];
        if (passages.Length == 0) body.Children.Add(label("Aucun passage documentaire enregistré pour cette réponse."));
        TextBlock Entry(string text, string style)
        {
            var entry = label(text);
            entry.Classes.Add(style);
            return entry;
        }
        if (diagnostics?.HasSystemInstructions == true) body.Children.Add(label("◆ Consignes protégées présentes"));
        if (diagnostics?.HistoryMessages is > 0) body.Children.Add(Entry($"◷ Historique · {diagnostics.HistoryMessages} messages dans le contexte", "registryHistory"));
        foreach (var memory in diagnostics?.Citations?.Historical ?? diagnostics?.Temporal?.Sources ?? [])
        {
            body.Children.Add(Entry($"◷ Mémoire · {(memory.Role == "user" ? "Vous" : "Alycia")} · [{memory.Reference}] · {memory.CreatedAtUtc.ToLocalTime():g}", "registryMemory"));
            if (memory.ConversationId == conversation) body.Children.Add(actionButton($"Ouvrir le message {memory.Sequence}", () => readHistory(_passage, conversation, memory.Sequence, memory.Reference)));
        }
        foreach (var guide in diagnostics?.Guides ?? [])
        {
            body.Children.Add(Entry($"◇ Synthèse dérivée dans le contexte · [{guide.Reference}] · {guide.Identity} · {guide.Tokens} tokens{(guide.Partial ? " · partielle" : "")}", "registrySummary"));
            if (guide.Sha256 is { } sha && guide.Path is { } path)
                body.Children.Add(actionButton($"Ouvrir l’original · {path}", () => ReadPassageAsync(
                    new(guide.OriginalReference, guide.FileId, path, sha, 0, 0, guide.Start, guide.End, "guide", false))));
        }
        foreach (var source in passages)
        {
            var lines = source.StartLine > 0 ? $" · l. {source.StartLine}–{source.EndLine}" : "";
            var origin = source.Origin switch { "initial" => "extrait dans le contexte", "tool" => "sortie d’outil présente", "tool_compacted" => "extrait retiré pendant la réduction des outils", _ => "vérification après réponse" };
            var detail = source.Scope is null ? "" : $" · {source.Scope} · {source.Tokens} tokens";
            if (source.Score is double score) detail += $" · score {score:F2}";
            if (!string.IsNullOrWhiteSpace(source.Modified)) detail += " · modification " + source.Modified;
            if (!string.IsNullOrWhiteSpace(source.Created)) detail += " · création " + source.Created;
            if (source.Uploaded is { } imported) detail += $" · import {imported:O}";
            body.Children.Add(Entry($"▤ Document · {origin}{detail}", "registryDocument"));
            body.Children.Add(actionButton($"[{source.Reference}] {source.Path}{lines}", () => ReadPassageAsync(source)));
        }
        if (diagnostics?.Retrieval?.Classification is { } classification)
        {
            var classified = new StackPanel { Spacing = 6 };
            foreach (var hit in classification.Candidates)
            {
                var used = passages.Any(s => s.Origin == "initial" && (s.RankedReference ?? s.Reference) == hit.Reference);
                var facets = string.Join(", ", hit.Facets.Select(p => $"{p.Key} {p.Value:F2}"));
                var state = hit.Relevance switch { "relevant" => "pertinent", "irrelevant" => "hors sujet", _ => "incertain" };
                classified.Children.Add(Entry($"{hit.Reference} · {state} · {(used ? "utilisé" : "seulement classé")} · {facets}"
                    + (hit.Relation == "contradicts" ? " · contradiction avec l’affirmation" : ""), hit.Relation == "contradicts" ? "registryWarning" : "registryDocument"));
            }
            body.Children.Add(new Expander { Header = $"Classement · {classification.Pairs} paires", Content = classified });
        }
        body.Children.Add(_passage);
        if (message?.HasDiagnostics == true) body.Children.Add(new Expander { Header = "Détails de la réponse", Content = new MarkdownView { Text = ResponseDiagnostics.Describe(diagnostics), IsMarkdown = false } });
        if (message?.HasToolTrace == true) body.Children.Add(new Expander { Header = "Appels d’outils", Content = new MarkdownView { Text = message.ToolTraceText, IsMarkdown = false } });
    }
}
