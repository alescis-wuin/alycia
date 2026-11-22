using System.Text.Json;

namespace Alycia.Contracts.Chat;

public sealed record ConversationSettingsFile<T>(string Format, int Version, string Model,
    string SystemPrompt, string ContextNotes, T Options) where T : ChatSettings, new();

public static class SettingsFileCodec<T> where T : ChatSettings, new()
{
    public const int MaxBytes = 256 * 1024;
    private const string Format = "alycia-conversation-settings";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, PropertyNameCaseInsensitive = false, MaxDepth = 16 };
    // A format version contains every persisted option. Add a migration/version when changing this contract.
    private static readonly JsonElement Shape = JsonSerializer.SerializeToElement(new ConversationSettingsFile<T>(Format, 1, "", "", "", new()), Json);

    public static byte[] Export(string model, string systemPrompt, string contextNotes, T options,
        Action<ConversationSettingsFile<T>>? validateSettings = null)
    {
        var file = new ConversationSettingsFile<T>(Format, 1, model, systemPrompt, contextNotes, options);
        Validate(file, validateSettings);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(file, Json);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Configuration limitée à 256 Kio.");
        return bytes;
    }

    public static ConversationSettingsFile<T> Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaxBytes) throw new ArgumentException("Configuration limitée à 256 Kio.");
        if (bytes.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) bytes = bytes[3..];
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            CheckShape(document.RootElement, Shape);
            var file = document.RootElement.Deserialize<ConversationSettingsFile<T>>(Json)
                ?? throw new ArgumentException("Configuration vide.");
            Validate(file, null);
            return file;
        }
        catch (JsonException) { throw new ArgumentException("Fichier JSON de configuration invalide."); }
    }

    private static void CheckShape(JsonElement value, JsonElement expected)
    {
        if (expected.ValueKind != JsonValueKind.Object) return;
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Configuration incomplète ou incompatible.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name) || !expected.TryGetProperty(property.Name, out var field))
                throw new ArgumentException("Configuration contenant un champ inconnu ou dupliqué.");
            CheckShape(property.Value, field);
        }
        if (names.Count != expected.EnumerateObject().Count())
            throw new ArgumentException("Configuration incomplète : exportez à nouveau les réglages depuis Alycia.");
    }

    private static void Validate(ConversationSettingsFile<T> file, Action<ConversationSettingsFile<T>>? validateSettings)
    {
        if (file.Format != Format || file.Version != 1) throw new ArgumentException("Format ou version de configuration non pris en charge.");
        if (file.Options is null || file.SystemPrompt is null || file.ContextNotes is null)
            throw new ArgumentException("Configuration incomplète.");
        file.Options.Validate();
        if (validateSettings is not null)
        {
            validateSettings(file);
            return;
        }
        SettingsInput.ValidateWindow(file.Model, file.SystemPrompt, file.ContextNotes, file.Options, canonical: true);
        if (file.Options.UseProjectTools) ToolWindowBudget.SafetyTokens(file.Options, SettingsInput.Estimate);
    }
}
