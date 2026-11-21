using System.Text.Json;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class ConversationConfiguration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ChatOptions Options(Conversation conversation, ChatOptions defaults)
    {
        // Existing conversations retain server defaults until explicitly configured.
        var options = conversation.SettingsJson == "{}" ? defaults.Copy()
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

    public static string System(Conversation conversation, ChatOptions? options = null) => (conversation.ProjectSnapshot?.SystemText() ?? "") + conversation.SystemPrompt
        + (conversation.ContextNotes.Length == 0 ? "" : "\n\nNotes de référence fournies par l’utilisateur :\n" + conversation.ContextNotes)
        + (options?.Route?.Mode == "unavailable" ? "\n\nLe fichier demandé est indisponible pour cet échange. Signale cette limite et demande le document si nécessaire." : "")
        + (options?.SearchPlan?.Ambiguous == true ? "\n\nAucun antécédent unique pour la référence de cette question. Demande une précision si la réponse en dépend." : "");

    public static void CheckRevision(Conversation conversation, int revision)
    {
        if (conversation.Revision != revision) throw new ChatConflictException();
    }
}
