using System.Text.Json;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class ConversationConfiguration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ChatOptions Options(Conversation conversation, ChatOptions defaults)
    {
        // Existing conversations retain server defaults until explicitly configured.
        var options = conversation.SettingsJson == "{}" ? defaults
            : JsonSerializer.Deserialize<ChatOptions>(conversation.SettingsJson, Json)
                ?? throw new ArgumentException("Réglages de conversation invalides.");
        options.Validate();
        return options;
    }

    public static string Serialize(ChatOptions options)
    {
        options.Validate();
        return JsonSerializer.Serialize(options, Json);
    }

    public static string System(Conversation conversation) => (conversation.ProjectSnapshot?.SystemText() ?? "") + conversation.SystemPrompt
        + (conversation.ContextNotes.Length == 0 ? "" : "\n\nNotes de référence fournies par l’utilisateur :\n" + conversation.ContextNotes);

    public static void CheckRevision(Conversation conversation, int revision)
    {
        if (conversation.Revision != revision) throw new ChatConflictException();
    }
}
