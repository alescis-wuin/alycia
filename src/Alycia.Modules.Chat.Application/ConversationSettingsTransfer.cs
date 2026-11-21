using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class ConversationSettingsTransfer
{
    public const int MaxBytes = SettingsFileCodec<ChatOptions>.MaxBytes;
    public static byte[] Export(string model, string systemPrompt, string contextNotes, ChatOptions options) =>
        SettingsFileCodec<ChatOptions>.Export(model, systemPrompt, contextNotes, options, file =>
        {
            var sample = Conversation.Create("Configuration", file.Model, file.SystemPrompt);
            sample.Configure(file.Model, file.SystemPrompt, file.ContextNotes, ConversationConfiguration.Serialize(file.Options));
            if (sample.Model != file.Model || sample.SystemPrompt != file.SystemPrompt || sample.ContextNotes != file.ContextNotes)
                throw new ArgumentException("Les textes de configuration contiennent des espaces de bord non canoniques.");
            SlidingContext.Build(ConversationConfiguration.System(sample), [], "Bonjour", file.Options);
            if (file.Options.UseProjectTools) ProjectAgent.ContextOptions(file.Options);
        });

    public static ConversationSettingsFile<ChatOptions> Parse(ReadOnlyMemory<byte> bytes) =>
        SettingsFileCodec<ChatOptions>.Parse(bytes);
}
