using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public sealed partial class ChatApiClient
{
    public Task<ImportLimits> ImportLimitsAsync(CancellationToken ct) => SendAsync<ImportLimits>(HttpMethod.Get, "/import-settings", null, ct);
    public Task<ImportLimits> SaveImportLimitsAsync(ImportLimits limits, CancellationToken ct) => SendAsync<ImportLimits>(HttpMethod.Put, "/import-settings", limits, ct);
}

public partial class ChatWindow
{
    private async Task EditImportLimitsAsync()
    {
        var limits = await _client.ImportLimitsAsync(_lifetime.Token);
        var fields = new (string Label, int Value)[]
        {
            ("Taille maximale par fichier · Mio (1–256)", limits.FileMiB),
            ("Taille décompressée par lot · Mio (jusqu’à 1024)", limits.BatchMiB),
            ("Entrées par lot, archives comprises (1–10 000)", limits.BatchEntries),
            ("Fichiers stockés par projet ou conversation (0 : sans quota)", limits.StoredFiles),
            ("Stockage par projet ou conversation · Mio (0 : sans quota)", limits.StoredMiB),
            ("Profondeur des archives (1–16)", limits.ArchiveDepth),
            ("Profondeur des dossiers (1–64)", limits.FolderDepth)
        };
        var inputs = fields.Select(f => Input(f.Value.ToString(), 8)).ToArray();
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Label("Ces réglages s’appliquent aux prochains imports de cette installation. Réduire un quota ne supprime aucun fichier existant."));
        for (var i = 0; i < fields.Length; i++) { body.Children.Add(Label(fields[i].Label)); body.Children.Add(inputs[i]); }
        ImportLimits? edited = null;
        string? Validate()
        {
            var values = new int[inputs.Length];
            for (var i = 0; i < values.Length; i++) if (!int.TryParse(inputs[i].Text, out values[i])) return "Un nombre entier est attendu.";
            edited = new(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
            try { edited.Validate(); return null; }
            catch (ArgumentException error) { return error.Message; }
        }
        if (!await EditDialogAsync("Imports et stockage", body, validate: Validate)) return;
        await _client.SaveImportLimitsAsync(edited!, _lifetime.Token);
        Feedback("Limites d’import enregistrées pour toute l’installation.");
    }
}
