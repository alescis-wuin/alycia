using System;
using System.Linq;
using System.Threading.Tasks;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private async void CitationClick(object? sender, CitationEventArgs e)
    {
        if (_current?.Id != e.Message.ConversationId) return;
        _evidence = e.Message;
        await RunAsync(async () =>
        {
            await OpenInspectorAsync("sources");
            var passage = C<StackPanel>("InspectorBody").Children.OfType<MarkdownView>().First();
            await ReadPassageAsync(passage, e.Message.ConversationId, e.Source);
        });
    }

    private async Task ReadPassageAsync(MarkdownView passage, Guid conversation, SuppliedSource source)
    {
        try
        {
            var version = await _client.SourceAsync(conversation, false, source.FileId, _lifetime.Token, source.Sha256, source.Start, source.End);
            var truncated = version.End < source.End ? "\nExtrait affiché partiellement." : "";
            passage.Text = $"{version.Name}\nSHA-256 {version.Sha256}{truncated}\n\n{version.Text}";
            passage.BringIntoView();
        }
        catch (ChatApiException e) { passage.Text = "Passage indisponible. " + e.Message; }
    }

    private async Task FillInspectorAsync(StackPanel body, string tab)
    {
        if (_current is not { } chat) return;
        var ct = _lifetime.Token;
        if (tab == "sources")
        {
            var message = _evidence ?? _messages.LastOrDefault(m => m.IsAssistant);
            var diagnostics = message is null ? null : TurnDiagnostics.Read(message.DiagnosticsJson);
            if (diagnostics?.WorkspaceRevision is int revision) body.Children.Add(Label($"Projet au moment de la réponse · révision {revision}"));
            var passages = diagnostics?.Citations?.Supplied ?? [];
            if (passages.Length == 0) body.Children.Add(Label("Aucun passage documentaire enregistré pour cette réponse."));
            static TextBlock Entry(string text, string style)
            {
                var label = Label(text); label.Classes.Add(style); return label;
            }
            if (diagnostics?.HasSystemInstructions == true) body.Children.Add(Label("◆ Consignes protégées présentes"));
            if (diagnostics?.HistoryMessages is > 0) body.Children.Add(Entry($"◷ Historique · {diagnostics.HistoryMessages} messages dans le contexte", "registryHistory"));
            var passage = new MarkdownView { IsMarkdown = false };
            foreach (var memory in diagnostics?.Citations?.Historical ?? diagnostics?.Temporal?.Sources ?? [])
            {
                body.Children.Add(Entry($"◷ Mémoire · {(memory.Role == "user" ? "Vous" : "Alycia")} · [{memory.Reference}] · {memory.CreatedAtUtc.ToLocalTime():g}", "registryMemory"));
                if (memory.ConversationId == chat.Id) body.Children.Add(ActionButton($"Ouvrir le message {memory.Sequence}", () => ReadHistoryAsync(passage, chat.Id, memory.Sequence, memory.Reference)));
            }
            foreach (var guide in diagnostics?.Guides ?? [])
            {
                body.Children.Add(Entry($"◇ Synthèse dérivée dans le contexte · [{guide.Reference}] · {guide.Identity} · {guide.Tokens} tokens{(guide.Partial ? " · partielle" : "")}", "registrySummary"));
                if (guide.Sha256 is { } sha && guide.Path is { } path)
                    body.Children.Add(ActionButton($"Ouvrir l’original · {path}", () => ReadPassageAsync(passage, chat.Id,
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
                body.Children.Add(ActionButton($"[{source.Reference}] {source.Path}{lines}", () => ReadPassageAsync(passage, chat.Id, source)));
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
            body.Children.Add(passage);
            if (message?.HasDiagnostics == true) body.Children.Add(new Expander { Header = "Détails de la réponse", Content = new MarkdownView { Text = message.DiagnosticsText, IsMarkdown = false } });
            if (message?.HasToolTrace == true) body.Children.Add(new Expander { Header = "Appels d’outils", Content = new MarkdownView { Text = message.ToolTraceText, IsMarkdown = false } });
            return;
        }
        if (tab == "context")
        {
            var draft = C<TextBox>("MessageInput").Text?.Trim() ?? "";
            if (draft.Length == 0) { body.Children.Add(Label("Saisissez un brouillon pour voir son contexte avant l’envoi.")); return; }
            var preview = await _client.PreviewAsync(chat.Id, draft, ct);
            var context = preview.Context;
            body.Children.Add(Label($"{context.EstimatedInputTokens:N0} tokens estimés · {context.HistoryMessagesIncluded} messages · {context.FilesIncluded}/{context.FilesAvailable} sources"));
            if (context.NeedsSummary) body.Children.Add(Label("Un résumé sera nécessaire avant l’envoi."));
            foreach (var source in context.Sources ?? []) body.Children.Add(Label($"{source.Path} · {source.Reason}"));
            foreach (var omission in context.Retrieval?.Omissions ?? []) body.Children.Add(Label(omission));
            body.Children.Add(new Expander
            {
                Header = "Messages préparés",
                Content = new MarkdownView
                {
                    IsMarkdown = false,
                    Text = string.Join("\n\n", context.Messages.Select(m => $"{m.Role}\n{m.Content}"))
                }
            });
            return;
        }
        var current = (await _client.SettingsAsync(chat.Id, ct)).Conversation;
        var entries = StructuredMemory.Read(current.MemoryLedgerJson).Entries;
        body.Children.Add(Label("Mémoire privée de la conversation"));
        var history = new StackPanel { Spacing = 6 };
        body.Children.Add(ActionButton("Synthèses des échanges", () => ShowHistoryNotesAsync(history, chat.Id)));
        body.Children.Add(history);
        if (current.MemoryText.Length > 0) body.Children.Add(new MarkdownView { Text = current.MemoryText });
        if (entries.Length == 0 && current.MemoryText.Length == 0) body.Children.Add(Label("Aucune mémoire enregistrée."));
        var project = current.WorkspaceId is Guid id ? await _client.ProjectAsync(id, ct) : null;
        foreach (var entry in entries)
        {
            var content = new StackPanel { Spacing = 8, Children = { Label(entry.Quote) } };
            if (project is { Archived: false }) content.Children.Add(ActionButton("Promouvoir en note partagée…",
                () => EditProjectNoteAsync(project, null, entry.Quote, current.Id, entry.Sequence, () => OpenInspectorAsync("memory"))));
            body.Children.Add(new Expander { Header = $"Message {entry.Sequence} · {entry.Kind}", Content = content });
        }
        body.Children.Add(ActionButton("Gérer le résumé et la mémoire", MemoryAsync));
        if (project is not null) AddProjectNotes(body, project, () => OpenInspectorAsync("memory"));
    }

    private void AddProjectNotes(StackPanel body, ProjectSettings project, Func<Task> refresh)
    {
        body.Children.Add(Label("Notes partagées du projet"));
        foreach (var note in project.Notes)
        {
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(Label(note.Text));
            if (note.OriginConversationId is Guid origin) panel.Children.Add(Label($"Conversation {origin} · message {note.OriginSequence}"));
            if (!project.Archived)
            {
                panel.Children.Add(ActionButton("Modifier", () => EditProjectNoteAsync(project, note, note.Text, null, null, refresh)));
                panel.Children.Add(ActionButton("Retirer la note…", async () =>
                {
                    if (!await EditDialogAsync("Retirer la note partagée", Label(note.Text), "Retirer")) return;
                    await _client.DeleteNoteAsync(project, note.Id, _lifetime.Token);
                    await refresh();
                }));
            }
            body.Children.Add(new Expander { Header = note.Text.Split('\n')[0], Content = panel });
        }
        if (!project.Archived) body.Children.Add(ActionButton("Ajouter une note partagée…", () => EditProjectNoteAsync(project, null, "", null, null, refresh)));
    }

    private async Task EditProjectNoteAsync(ProjectSettings project, WorkspaceNote? note, string text, Guid? origin, int? sequence, Func<Task> refresh)
    {
        var edit = Input(text, 1500, true);
        if (!await EditDialogAsync("Note partagée avec les conversations du projet", edit, "Enregistrer", () =>
            string.IsNullOrWhiteSpace(edit.Text) ? "Saisissez le texte à partager." : null)) return;
        await _client.SaveNoteAsync(project, note?.Id, edit.Text!, origin, sequence, _lifetime.Token);
        await refresh();
    }
}
