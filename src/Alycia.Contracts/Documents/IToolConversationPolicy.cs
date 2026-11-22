using Alycia.Contracts.Chat;
using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Documents;

public sealed record ToolConversationPolicy(bool ToolsEnabled, int MessageCount, int? FirstVisibleSequence,
    EmbeddingQuery? HistoryEmbedding)
{
    public static ToolConversationPolicy Capture(ChatSettings settings, int messages)
    {
        var restrictFiles = settings.AgentPlanningEnabled || settings.RoutingMode != "off" || settings.VerificationSearchEnabled;
        var historyTurns = settings.AutoSummarize ? settings.ProtectedHistoryTurns : settings.MaxHistoryTurns;
        return new(settings.UseProjectTools, messages,
            restrictFiles ? Math.Max(1, messages - 2 * historyTurns + 1) : null,
            settings.RetrievalMode == "hybrid" && settings.EmbedHistory ? EmbeddingQuery.Capture(settings) : null);
    }
}

public interface IToolConversationPolicy
{
    Task<ToolConversationPolicy?> ReadAsync(Guid conversation, CancellationToken ct);
    Task<bool> HasHistoryAsync(Guid conversation, CancellationToken ct);
}
