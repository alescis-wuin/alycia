namespace Alycia.Modules.Chat.Application;

public static class ConversationSettingsTransfer
{
    public const int MaxBytes = SettingsFileCodec<ChatOptions>.MaxBytes;
    public static byte[] Export(string model, string systemPrompt, string contextNotes, ChatOptions options) =>
        SettingsFileCodec<ChatOptions>.Export(model, systemPrompt, contextNotes, options, file =>
        {
            var context = file.Options.Context();
            SettingsInput.ValidateWindow(file.Model, file.SystemPrompt, file.ContextNotes, file.Options, true, context.Count);
            if (file.Options.UseProjectTools) ToolWindowBudget.SafetyTokens(context, context.Count);
        });

    public static ConversationSettingsFile<ChatOptions> Parse(ReadOnlyMemory<byte> bytes) =>
        SettingsFileCodec<ChatOptions>.Parse(bytes);
}
