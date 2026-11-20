using System.Text.Json;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record ConversationSettingsFile(string Format, int Version, string Model,
    string SystemPrompt, string ContextNotes, ChatOptions Options);

public static class ConversationSettingsTransfer
{
    public const int MaxBytes = 256 * 1024;
    private const string Format = "alycia-conversation-settings";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, PropertyNameCaseInsensitive = false, MaxDepth = 16 };
    // A format version contains every persisted option. Add a migration/version when changing this contract.
    private static readonly JsonElement Shape = JsonSerializer.SerializeToElement(new ConversationSettingsFile(Format, 1, "", "", "", new()), Json);

    public static byte[] Export(string model, string systemPrompt, string contextNotes, ChatOptions options)
    {
        var file = new ConversationSettingsFile(Format, 1, model, systemPrompt, contextNotes, options);
        Validate(file);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(file, Json);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Configuration limitée à 256 Kio.");
        return bytes;
    }

    public static ConversationSettingsFile Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaxBytes) throw new ArgumentException("Configuration limitée à 256 Kio.");
        if (bytes.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) bytes = bytes[3..];
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            CheckShape(document.RootElement, Shape);
            var file = document.RootElement.Deserialize<ConversationSettingsFile>(Json)
                ?? throw new ArgumentException("Configuration vide.");
            Validate(file);
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

    private static void Validate(ConversationSettingsFile file)
    {
        if (file.Format != Format || file.Version != 1) throw new ArgumentException("Format ou version de configuration non pris en charge.");
        if (file.Options is null || file.SystemPrompt is null || file.ContextNotes is null)
            throw new ArgumentException("Configuration incomplète.");
        file.Options.Validate();
        var sample = Conversation.Create("Configuration", file.Model, file.SystemPrompt);
        sample.Configure(file.Model, file.SystemPrompt, file.ContextNotes, ConversationConfiguration.Serialize(file.Options));
        if (sample.Model != file.Model || sample.SystemPrompt != file.SystemPrompt || sample.ContextNotes != file.ContextNotes)
            throw new ArgumentException("Les textes de configuration contiennent des espaces de bord non canoniques.");
        SlidingContext.Build(ConversationConfiguration.System(sample), [], "Bonjour", file.Options);
        if (file.Options.UseProjectTools) ProjectAgent.ContextOptions(file.Options);
    }
}
