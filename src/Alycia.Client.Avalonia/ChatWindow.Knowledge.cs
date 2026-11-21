using static Alycia.Client.Avalonia.DialogFields;
using System;
using System.Linq;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{

    private async Task ReadHistoryAsync(MarkdownView view, Guid owner, int sequence, string reference)
    {
        var text = await new KnowledgeBrowser(_client).ReadOriginalAsync(owner, sequence, reference, _lifetime.Token);
        view.Text = text ?? "L’original de cette référence est indisponible.";
        if (text is not null) view.BringIntoView();
    }

    private async Task ShowHistoryNotesAsync(StackPanel panel, Guid owner, int before = int.MaxValue, string? group = null)
    {
        var page = await new KnowledgeBrowser(_client).HistoryAsync(owner, before, group, _lifetime.Token);
        panel.Children.Clear();
        var view = new MarkdownView { IsMarkdown = false };
        if (page.Items.Length == 0) panel.Children.Add(Label("Aucune synthèse d’échanges publiée."));
        foreach (var note in page.Items)
        {
            var summary = new StackPanel { Spacing = 5, Children = { new MarkdownView { Text = note.Summary, IsMarkdown = false } } };
            if (group is null) summary.Children.Add(ActionButton("Messages de ce groupe", () => ShowHistoryNotesAsync(panel, owner, before, note.Key)));
            else if (note.Reference is not null) summary.Children.Add(ActionButton("Ouvrir le message original", () => ReadHistoryAsync(view, owner, note.Sequence, note.Reference)));
            panel.Children.Add(new Expander { Header = note.Heading, Content = summary });
        }
        if (group is not null) panel.Children.Add(ActionButton("Groupes de messages", () => ShowHistoryNotesAsync(panel, owner, before)));
        else if (page.NextBefore is int next) panel.Children.Add(ActionButton("Groupes précédents", () => ShowHistoryNotesAsync(panel, owner, next)));
        panel.Children.Add(view);
    }

    private async Task ShowNoteAsync(StackPanel panel, Guid owner, bool project, Guid file, string? key)
    {
        panel.Children.Clear();
        KnowledgeDocument document;
        try { document = await new KnowledgeBrowser(_client).DocumentAsync(owner, project, file, key, _lifetime.Token); }
        catch (ChatApiException) { panel.Children.Add(Label("Synthèse indisponible pour ce document.")); return; }
        panel.Children.Add(Label(document.Heading));
        panel.Children.Add(new MarkdownView { Text = document.Summary, IsMarkdown = false });
        panel.Children.Add(Label(document.Metadata));
        var details = new StackPanel { Spacing = 4 };
        foreach (var detail in document.Details) details.Children.Add(Label(detail));
        panel.Children.Add(new Expander { Header = "Annotations et provenance", Content = details });
        if (document.Children.Length == 0) panel.Children.Add(Label(string.Join(" · ", document.Sources)));
        for (var i = 0; i < document.Children.Length; i++)
        {
            var child = document.Children[i];
            panel.Children.Add(ActionButton($"Ouvrir l’unité {i + 1}", () => ShowNoteAsync(panel, owner, project, file, child)));
        }
        if (key is not null) panel.Children.Add(ActionButton("Synthèse du document", () => ShowNoteAsync(panel, owner, project, file, null)));
    }
}
