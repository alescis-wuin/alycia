using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record ArchivedMessage(string Role, string Content, DateTimeOffset CreatedAtUtc, string ToolTraceJson = "[]", string DiagnosticsJson = "{}");
public sealed record ConversationArchive(int SchemaVersion, string Title, string Model, string SystemPrompt,
    string ContextNotes, ChatOptions Options, ArchivedMessage[] Messages, Guid? SourceWorkspaceId = null, string[]? ReferencedProjectFiles = null);

public static class ConversationTransfer
{
    public const int MaxBytes = ChatArchiveLimits.MaxBytes;
    public const int MaxMessages = 2000;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, MaxDepth = 16 };

    public static ConversationArchive Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new ArgumentException("Archive de conversation limitée à 5 Mio.");
        try
        {
            var archive = JsonSerializer.Deserialize<ConversationArchive>(bytes, Json)
                ?? throw new ArgumentException("Archive de conversation vide.");
            Validate(archive);
            return archive;
        }
        catch (JsonException) { throw new ArgumentException("Archive JSON invalide."); }
    }

    public static void Validate(ConversationArchive archive)
    {
        if (archive.SchemaVersion != 1 || archive.Options is null || archive.Messages is null
            || archive.Messages.Length > MaxMessages || archive.Messages.Length % 2 != 0)
            throw new ArgumentException("Version, réglages ou échanges de l’archive invalides.");
        var conversation = Conversation.Create(archive.Title, archive.Model, archive.SystemPrompt);
        conversation.Configure(archive.Model, archive.SystemPrompt, archive.ContextNotes,
            ConversationConfiguration.Serialize(archive.Options));
        SlidingContext.ValidatePrompt(ConversationConfiguration.System(conversation), "Bonjour", archive.Options.Context());
        for (var i = 0; i < archive.Messages.Length; i++)
        {
            var message = archive.Messages[i];
            if (message is null || message.Role != (i % 2 == 0 ? "user" : "assistant")
                || string.IsNullOrWhiteSpace(message.Content) || message.Content.Contains('\0')
                || message.Content.Length > (i % 2 == 0 ? 32000 : 1_000_000))
                throw new ArgumentException("L’archive doit contenir des échanges user/assistant complets et bornés.");
            if (message.DiagnosticsJson is null || message.DiagnosticsJson.Length > 2_000_000) throw new ArgumentException("Diagnostic invalide.");
            try { using var diagnostic = JsonDocument.Parse(message.DiagnosticsJson); if (diagnostic.RootElement.ValueKind != JsonValueKind.Object || (message.Role == "user" && message.DiagnosticsJson != "{}")) throw new ArgumentException("Diagnostic invalide."); }
            catch (JsonException) { throw new ArgumentException("Diagnostic invalide."); }
            if (message.ToolTraceJson is null || message.ToolTraceJson.Length > 512000) throw new ArgumentException("Trace d’outils trop volumineuse.");
            try
            {
                var trace = JsonSerializer.Deserialize<ToolObservation[]>(message.ToolTraceJson, ProjectToolCatalog.Json);
                if (trace is null || trace.Length > 128 || trace.Any(t => t is null) || message.Role == "user" && trace.Length > 0)
                    throw new ArgumentException("Trace d’outils invalide.");
            }
            catch (JsonException) { throw new ArgumentException("Trace d’outils JSON invalide."); }
        }
    }

    public static (Conversation Conversation, ChatMessage[] Messages) Restore(ConversationArchive archive)
    {
        Validate(archive);
        var conversation = Conversation.Create(archive.Title, archive.Model, archive.SystemPrompt);
        conversation.Configure(archive.Model, archive.SystemPrompt, archive.ContextNotes,
            ConversationConfiguration.Serialize(archive.Options));
        var messages = new List<ChatMessage>();
        for (var i = 0; i < archive.Messages.Length; i += 2)
        {
            var request = Guid.NewGuid();
            for (var j = i; j < i + 2; j++)
            {
                var m = archive.Messages[j];
                var message = ChatMessage.Import(conversation.Id, j + 1, request, m.Role, m.Content, m.CreatedAtUtc);
                if (m.Role == "assistant") { message.SetToolTrace(m.ToolTraceJson); message.SetDiagnostics(m.DiagnosticsJson); }
                messages.Add(message);
            }
            conversation.CompleteTurn();
        }
        return (conversation, messages.ToArray());
    }

    public static byte[] ExportJson(ConversationArchive archive)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(archive, Json);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Export supérieur à 5 Mio : utilisez la sauvegarde complète.");
        return bytes;
    }

    public static string Markdown(ConversationArchive archive)
    {
        var text = new StringBuilder().Append("# ").AppendLine(archive.Title).AppendLine()
            .Append("Modèle : ").AppendLine(archive.Model).AppendLine()
            .AppendLine("## Instruction système").AppendLine().AppendLine(archive.SystemPrompt).AppendLine()
            .AppendLine("## Notes de contexte").AppendLine().AppendLine(archive.ContextNotes).AppendLine();
        if (archive.SourceWorkspaceId is Guid project)
        {
            text.Append("Projet d’origine : ").AppendLine(project.ToString());
            text.AppendLine("Documents référencés uniquement. Les originaux, consignes et notes du projet restent dans la sauvegarde complète.");
            foreach (var file in archive.ReferencedProjectFiles ?? []) text.Append("- ").AppendLine(file);
            text.AppendLine();
        }
        foreach (var message in archive.Messages)
            text.Append("## ").Append(message.Role == "user" ? "Vous" : "Alycia").Append(" · ")
                .AppendLine(message.CreatedAtUtc.ToString("O")).AppendLine().AppendLine(message.Content).AppendLine();
        return text.ToString();
    }
}
