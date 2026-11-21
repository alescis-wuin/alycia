using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

internal sealed record KnowledgeDocument(string Heading, string Summary, string Metadata, string[] Details, string[] Sources, string[] Children);
internal sealed record HistoryNote(string Heading, string Summary, string Key, int Sequence, string? Reference);
internal sealed record HistoryNotes(HistoryNote[] Items, int? NextBefore);

internal sealed class KnowledgeBrowser(ChatApiClient client)
{
    internal async Task<string?> ReadOriginalAsync(Guid owner, int sequence, string reference, CancellationToken ct)
    {
        var page = await client.MessagesAsync(owner, checked(sequence + 1), ct);
        var source = page.Messages.FirstOrDefault(m => m.ConversationId == owner && m.Sequence == sequence);
        if (source is null || reference != $"H{sequence}:{MemoryTransfer.Hash(source.Content)[..12]}") return null;
        return $"{(source.IsAssistant ? "Alycia" : "Vous")} · {source.CreatedAtUtc.ToLocalTime():g}\n\n{source.Content}";
    }

    internal async Task<HistoryNotes> HistoryAsync(Guid owner, int before, string? group, CancellationToken ct)
    {
        var notes = await client.KnowledgeHistoryAsync(owner, before, group, ct);
        var entries = notes.Select(note =>
        {
            var role = note.Role == "user" ? "Vous" : "Alycia";
            var heading = group is null ? $"Messages {note.First}–{note.Last} · {role}" : $"Message {note.First} · {role}";
            return new HistoryNote(heading, note.Labels.Summary, note.Key, note.First, note.Labels.SourceUnitIds.FirstOrDefault());
        }).ToArray();
        return new(entries, group is null && notes.Length == 24 ? notes[^1].Last : null);
    }

    internal async Task<KnowledgeDocument> DocumentAsync(Guid owner, bool project, Guid file, string? key, CancellationToken ct)
    {
        var unit = await client.KnowledgeAsync(owner, project, file, key, ct);
        var level = unit.Level switch { "document" => "Document", "section" => "Section", "passage" => "Passage", _ => unit.Level };
        var state = unit.State switch { "generated" => "synthèse du modèle", "extractive" => "extraction", "extractive_fallback" => "extraction de secours", "timeout_fallback" => "extraction de secours · délai de synthèse dépassé", "empty" => "vide", _ => unit.State };
        var details = new List<string>();
        void Detail(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) details.Add(name + " · " + value);
        }
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
        return new($"{unit.Labels.Title} · {level} · {state}", unit.Labels.Summary, $"{unit.Labels.Terms()}\n{unit.Identity} · {unit.IndexedAt:O}",
            details.ToArray(), unit.Labels.SourceUnitIds, unit.Children);
    }
}
