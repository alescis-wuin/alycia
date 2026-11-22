using System.Collections.Immutable;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Tools;

public sealed record ToolSourceHint(string Reference, string Path, string? Content);

public sealed record ToolPrompt(ImmutableArray<PromptMessage> Messages, int SummaryCalls, ImmutableArray<ToolSourceHint> Sources);

public sealed record ToolGeneration(InferenceCall Inference, SummaryParameters Summary, bool EvidenceSelectionEnabled,
    bool AgentPlanningEnabled, bool HistoryOnly, int AgentPlanMaxCalls, int MaxSearchQueries, int ToolMaxCalls,
    int ToolContextTokens, int ToolResultBytes, int ToolTimeoutSeconds, ImmutableArray<string> Questions, string? Original);
