using Alycia.Contracts.Context;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class TurnDiagnosticsWriter
{
    public static GenerationUsage? ReadUsage(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("usage", out var usage)
                ? System.Text.Json.JsonSerializer.Deserialize<GenerationUsage>(usage.GetRawText(), ProjectToolCatalog.Json) : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    internal static void AttachToolTrace(ChatMessage assistant, IReadOnlyList<ToolObservation>? tools)
    {
        if (tools is { Count: > 0 })
            assistant.SetToolTrace(System.Text.Json.JsonSerializer.Serialize(tools, ProjectToolCatalog.Json));
    }

    internal static void AttachDiagnostics(ChatMessage assistant, Conversation conversation, GenerationReply generated,
        ContextWindow window, ChatOptions settings, GenerationUsage usage, ReviewedReply review)
    {
        assistant.SetDiagnostics(System.Text.Json.JsonSerializer.Serialize(new TurnDiagnostics(usage, settings.RetrievalReport, window.Memory, review.Citations, generated.Warnings,
            review.Claims, conversation.WorkspaceId, conversation.ProjectSnapshot?.Revision, settings.Route, window.Assembly, window.Temporal, generated.Research, review.Followup, window.Guides, window.HistoryMessagesIncluded,
            window.Messages.Any(m => m.Role == "system")), ProjectToolCatalog.Json));
    }
}
