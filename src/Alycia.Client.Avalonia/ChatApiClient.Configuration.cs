using System;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

public sealed partial class ChatApiClient
{
    public async Task<byte[]> ExportConfigurationAsync(Guid id, CancellationToken ct)
    {
        var settings = await SettingsAsync(id, ct);
        return SettingsFileCodec<ChatSettings>.Export(settings.Conversation.Model, settings.Conversation.SystemPrompt,
            settings.Conversation.ContextNotes, settings.Options);
    }

    public Task<ConversationSummary> ImportConfigurationAsync(Guid id, int revision, byte[] bytes, CancellationToken ct)
    {
        var settings = SettingsFileCodec<ChatSettings>.Parse(bytes);
        return ConfigureAsync(id, revision, settings.Model, settings.SystemPrompt, settings.ContextNotes, settings.Options, ct);
    }
}
