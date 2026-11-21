using static Alycia.Client.Avalonia.DialogFields;
using System;
using System.Linq;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private EvidenceInspector? _visibleEvidence;

    private async void CitationClick(object? sender, CitationEventArgs e)
    {
        if (_current?.Id != e.Message.ConversationId) return;
        _evidence = e.Message;
        await RunAsync(async () =>
        {
            await OpenInspectorAsync("sources");
            if (_visibleEvidence is { } evidence) await evidence.OpenCitationAsync(e);
        });
    }

    private async Task FillInspectorAsync(StackPanel body, string tab)
    {
        if (_current is not { } chat) return;
        var ct = _lifetime.Token;
        if (tab == "sources")
        {
            var selected = new EvidenceSelection(chat.Id, _evidence, _messages);
            _visibleEvidence = new EvidenceInspector(selected, _client, ct);
            _visibleEvidence.Render(body, Label, ActionButton, ReadHistoryAsync);
            return;
        }
        _visibleEvidence = null;
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
        var entries = MemoryTransfer.ReadLedger(current.MemoryLedgerJson).Entries;
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

    private async Task EditProjectNoteAsync(ProjectSettings project, WorkspaceNoteView? note, string text, Guid? origin, int? sequence, Func<Task> refresh)
    {
        var edit = Input(text, 1500, true);
        if (!await EditDialogAsync("Note partagée avec les conversations du projet", edit, "Enregistrer", () =>
            string.IsNullOrWhiteSpace(edit.Text) ? "Saisissez le texte à partager." : null)) return;
        await _client.SaveNoteAsync(project, note?.Id, edit.Text!, origin, sequence, _lifetime.Token);
        await refresh();
    }
}
