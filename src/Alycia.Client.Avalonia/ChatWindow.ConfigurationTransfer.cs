using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Modules.Chat.Application;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Alycia.Client.Avalonia;

public sealed partial class ChatApiClient
{
    public async Task<byte[]> ExportConfigurationAsync(Guid id, CancellationToken ct)
    {
        var settings = await SettingsAsync(id, ct);
        return ConversationSettingsTransfer.Export(settings.Conversation.Model, settings.Conversation.SystemPrompt,
            settings.Conversation.ContextNotes, settings.Options);
    }

    public Task<ConversationSummary> ImportConfigurationAsync(Guid id, int revision, byte[] bytes, CancellationToken ct)
    {
        var settings = ConversationSettingsTransfer.Parse(bytes);
        return ConfigureAsync(id, revision, settings.Model, settings.SystemPrompt, settings.ContextNotes, settings.Options, ct);
    }
}

public partial class ChatWindow
{
    private async Task ExportConfigurationAsync()
    {
        if (_current is not { } current) return;
        try
        {
            var bytes = await _client.ExportConfigurationAsync(current.Id, _lifetime.Token);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exporter les réglages de la discussion",
                SuggestedFileName = "alycia-reglages.json",
                FileTypeChoices = [new("Configuration Alycia") { Patterns = ["*.json"] }]
            });
            if (file is null) return;
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await output.WriteAsync(bytes, _lifetime.Token);
            Feedback("Réglages exportés, avec les instructions et notes de contexte.");
        }
        catch (ArgumentException error) { throw new ChatApiException(error.Message); }
    }

    private async Task ImportConfigurationAsync()
    {
        if (_current is not { } current) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importer les réglages dans cette discussion",
                AllowMultiple = false,
                FileTypeFilter = [new("Configuration Alycia") { Patterns = ["*.json"] }]
            });
            if (files.Count == 0) return;
            await using var input = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(block, _lifetime.Token)) > 0)
            {
                if (buffer.Length + count > ConversationSettingsTransfer.MaxBytes)
                    throw new ChatApiException("Configuration limitée à 256 Kio.");
                buffer.Write(block, 0, count);
            }
            var bytes = buffer.ToArray();
            var settings = ConversationSettingsTransfer.Parse(bytes);
            var preview = new StackPanel { Spacing = 10 };
            preview.Children.Add(Label($"Discussion cible : {current.Title}\nModèle : {settings.Model}"));
            preview.Children.Add(Label("Tous les réglages, instructions et notes de contexte seront remplacés. Les messages, documents et consignes du projet sont conservés."));
            if (settings.Model != current.Model)
                preview.Children.Add(Label("Le changement de modèle réinitialise le résumé généré de cette discussion."));
            preview.Children.Add(new Expander
            {
                Header = "Instructions et notes importées",
                Content = new MarkdownView
                { Text = settings.SystemPrompt + "\n\n" + settings.ContextNotes, IsMarkdown = false }
            });
            preview.Children.Add(new Expander
            {
                Header = "Tous les réglages importés",
                Content = new MarkdownView
                { Text = System.Text.Encoding.UTF8.GetString(bytes), IsMarkdown = false }
            });
            if (!await EditDialogAsync("Importer les réglages", preview, "Appliquer à cette discussion")) return;
            _current = await _client.ImportConfigurationAsync(current.Id, current.Revision, bytes, _lifetime.Token);
            await RefreshConversationsAsync(_current.Id);
            await LoadAsync(_current!);
            Feedback("Configuration importée à l’identique. Elle s’applique aux prochains échanges.");
        }
        catch (ArgumentException error) { throw new ChatApiException(error.Message); }
    }
}
