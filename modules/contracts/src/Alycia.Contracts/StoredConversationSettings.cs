using System.Text.Json;

namespace Alycia.Contracts.Chat;

public static class StoredConversationSettings
{
    public static T Read<T>(string json, Func<T> defaults) where T : ChatSettings
    {
        var options = json == "{}" ? defaults() : JsonSerializer.Deserialize<T>(json, ChatJson.Options)
            ?? throw new ArgumentException("Réglages de conversation invalides.");
        options.Validate();
        return options;
    }
}
