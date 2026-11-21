using static Alycia.Client.Avalonia.DialogFields;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using Alycia.Contracts.Chat;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public sealed partial class ChatApiClient
{
    public Task<IndexedNote[]> KnowledgeHistoryAsync(Guid owner, int before, string? group, CancellationToken ct)
        => SendAsync<IndexedNote[]>(HttpMethod.Get, $"/conversations/{owner}/index/knowledge/history?before={before}" + (group is null ? "" : "&group=" + group), null, ct);
    public Task<IndexedNote> KnowledgeAsync(Guid owner, bool project, Guid file, string? unit, CancellationToken ct)
        => SendAsync<IndexedNote>(HttpMethod.Get, $"/indexing/knowledge?owner={owner}&project={project}&file={file}" + (unit is null ? "" : "&unit=" + unit), null, ct);
}

public partial class ChatWindow
{

    private async Task ReadHistoryAsync(MarkdownView view, Guid owner, int sequence, string reference)
    {
        var page = await _client.MessagesAsync(owner, checked(sequence + 1), _lifetime.Token);
        var source = page.Messages.FirstOrDefault(m => m.ConversationId == owner && m.Sequence == sequence);
        if (source is null || reference != $"H{sequence}:{MemoryTransfer.Hash(source.Content)[..12]}")
        { view.Text = "L’original de cette référence est indisponible."; return; }
        view.Text = $"{(source.IsAssistant ? "Alycia" : "Vous")} · {source.CreatedAtUtc.ToLocalTime():g}\n\n{source.Content}";
        view.BringIntoView();
    }

    private async Task ShowHistoryNotesAsync(StackPanel panel, Guid owner, int before = int.MaxValue, string? group = null)
    {
        var notes = await _client.KnowledgeHistoryAsync(owner, before, group, _lifetime.Token);
        panel.Children.Clear();
        var view = new MarkdownView { IsMarkdown = false };
        if (notes.Length == 0) panel.Children.Add(Label("Aucune synthèse d’échanges publiée."));
        foreach (var note in notes)
        {
            var role = note.Role == "user" ? "Vous" : "Alycia";
            var summary = new StackPanel { Spacing = 5, Children = { new MarkdownView { Text = note.Labels.Summary, IsMarkdown = false } } };
            if (group is null) summary.Children.Add(ActionButton("Messages de ce groupe", () => ShowHistoryNotesAsync(panel, owner, before, note.Key)));
            else summary.Children.Add(ActionButton("Ouvrir le message original", () => ReadHistoryAsync(view, owner, note.First, note.Labels.SourceUnitIds[0])));
            var heading = group is null ? $"Messages {note.First}–{note.Last} · {role}" : $"Message {note.First} · {role}";
            panel.Children.Add(new Expander { Header = heading, Content = summary });
        }
        if (group is not null) panel.Children.Add(ActionButton("Groupes de messages", () => ShowHistoryNotesAsync(panel, owner, before)));
        else if (notes.Length == 24) panel.Children.Add(ActionButton("Groupes précédents", () => ShowHistoryNotesAsync(panel, owner, notes[^1].Last)));
        panel.Children.Add(view);
    }

    private async Task ShowNoteAsync(StackPanel panel, Guid owner, bool project, Guid file, string? key)
    {
        panel.Children.Clear();
        IndexedNote unit;
        try { unit = await _client.KnowledgeAsync(owner, project, file, key, _lifetime.Token); }
        catch (ChatApiException) { panel.Children.Add(Label("Synthèse indisponible pour ce document.")); return; }
        var level = unit.Level switch { "document" => "Document", "section" => "Section", "passage" => "Passage", _ => unit.Level };
        var state = unit.State switch { "generated" => "synthèse du modèle", "extractive" => "extraction", "extractive_fallback" => "extraction de secours", "timeout_fallback" => "extraction de secours · délai de synthèse dépassé", "empty" => "vide", _ => unit.State };
        panel.Children.Add(Label($"{unit.Labels.Title} · {level} · {state}"));
        panel.Children.Add(new MarkdownView { Text = unit.Labels.Summary, IsMarkdown = false });
        panel.Children.Add(Label($"{unit.Labels.Terms()}\n{unit.Identity} · {unit.IndexedAt:O}"));
        var details = new StackPanel { Spacing = 4 };
        void Detail(string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) details.Children.Add(Label(name + " · " + value)); }
        Detail("Description", unit.Labels.Description);
        Detail("Type", unit.Labels.DocumentType); Detail("Langue", unit.Labels.Language);
        Detail("Entités", string.Join(", ", unit.Labels.Entities));
        Detail("Symboles", string.Join(", ", unit.Labels.TechnicalSymbols));
        Detail("Faits extraits", string.Join("\n", unit.Labels.Facts));
        Detail("Incertitudes", string.Join("\n", unit.Labels.Uncertainties));
        Detail("Dates mentionnées", string.Join(", ", unit.Labels.DateMentions));
        Detail("Création de la source", unit.SourceCreated); Detail("Modification de la source", unit.SourceModified);
        Detail("Origine des dates", unit.SourceDateOrigin); Detail("Empreinte de l’entrée", unit.InputDigest);
        Detail("Traitement", $"{unit.Calls} appels · {unit.InputTokens} tokens d’entrée par essai · cible {unit.TargetTokens}");
        panel.Children.Add(new Expander { Header = "Annotations et provenance", Content = details });
        if (unit.Children.Length == 0) panel.Children.Add(Label(string.Join(" · ", unit.Labels.SourceUnitIds)));
        for (var i = 0; i < unit.Children.Length; i++)
        {
            var child = unit.Children[i];
            panel.Children.Add(ActionButton($"Ouvrir l’unité {i + 1}", () => ShowNoteAsync(panel, owner, project, file, child)));
        }
        if (key is not null) panel.Children.Add(ActionButton("Synthèse du document", () => ShowNoteAsync(panel, owner, project, file, null)));
    }
}
