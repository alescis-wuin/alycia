using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Alycia.Modules.Chat.Application;

public sealed record ContextWindow(IReadOnlyList<PromptMessage> Messages, int HistoryMessagesIncluded, int EstimatedInputTokens, int FilesIncluded = 0, bool NeedsSummary = false, int SummaryThroughSequence = 0, string SummaryText = "", int SummaryCalls = 0, IReadOnlyList<SourceExcerpt>? Sources = null, int FilesAvailable = 0, int ToolReserve = 0, RetrievalReport? Retrieval = null, MemoryReport? Memory = null, QueryRoute? Route = null, EvidenceAssembly? Assembly = null, TemporalReport? Temporal = null,
    [property: JsonIgnore] IReadOnlyList<EvidenceText>? MemoryTexts = null, UsedGuide[]? Guides = null)
{
    public ToolPrompt ToolPrompt() => new(PromptSnapshot.Capture(Messages), SummaryCalls,
        (Sources ?? []).Where(s => s.Reference is not null && s.SourceType != "summary")
            .Select(s => new ToolSourceHint(s.Reference!, s.Path, s.Content)).ToImmutableArray());
}
