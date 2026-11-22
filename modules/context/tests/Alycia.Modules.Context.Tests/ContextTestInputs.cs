using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Contracts.Documents;

namespace Alycia.Modules.Chat.Tests;

internal static class ContextTestInputs
{
    internal static TurnFile File() => new(Guid.Empty, null, null, Guid.Empty, null, "", 0, "", "", "",
        0, default, "", "", "unknown", "window");

    internal static ContextParameters Context(this ChatSettings s) => new(
        InferenceCall.Capture(s),
        new(s.ContextTokens, s.SafetyTokens, s.SummaryMaxCalls, s.Enrichment.MaxDepth, s.Seed, s.TimeoutSeconds, s.TokenCounting, null, null),
        s.ToolContextTokens, new(s.AutoSummarize, s.StructuredMemory, s.MaxHistoryTurns, s.ProtectedHistoryTurns, new(s.HistoryContextTokens, s.MemoryContextTokens)),
        new(s.EvidenceSelectionEnabled, s.ClassificationEnabled, s.RelevantThreshold, s.RetrievalMode, s.DocumentContextTokens,
            new(new(s.UseProjectTools, null, null, null, null), null, null, null)),
        new(s.QuerySummaryTokens, s.QuerySummaryMaxCalls, s.Enrichment));
}
