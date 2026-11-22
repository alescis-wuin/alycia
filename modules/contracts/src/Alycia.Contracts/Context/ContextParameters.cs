using Alycia.Contracts.Chat;
using Alycia.Contracts.Inference;
using Alycia.Contracts.Retrieval;

namespace Alycia.Contracts.Context;

public sealed record HistoryParameters(bool AutoSummarize, bool StructuredMemory, int MaxHistoryTurns,
    int ProtectedHistoryTurns, MemoryBudgetLimits Budget);

public sealed record EvidenceParameters(bool EvidenceSelectionEnabled, bool ClassificationEnabled,
    double RelevantThreshold, string RetrievalMode, int DocumentContextTokens, RoutingState Sources);

public sealed record GuideParameters(int Tokens, int MaxCalls, EnrichmentOptions Enrichment);

public sealed record ContextParameters(InferenceCall Inference, SummaryParameters Summary, int ToolContextTokens,
    HistoryParameters History, EvidenceParameters Evidence, GuideParameters Guides) : IToolWindowSettings
{
    public int ContextTokens => Inference.Generation.ContextTokens;
    public int MaxOutputTokens => Inference.Generation.MaxOutputTokens;
    public int SafetyTokens => Inference.SafetyTokens;
    public int AvailableInputTokens => ContextTokens - MaxOutputTokens - SafetyTokens;

    public ContextParameters WithSafetyTokens(int tokens)
    {
        if (tokens < 128 || (long)tokens + MaxOutputTokens >= ContextTokens)
            throw new ArgumentException("Configuration Chat invalide : vérifiez fenêtre, contexte, réserve et délai.");
        return this with { Inference = Inference with { SafetyTokens = tokens }, Summary = Summary with { SafetyTokens = tokens } };
    }

    public void ValidatePrompt(string system, string user)
    {
        if (PromptCost(system, user) > AvailableInputTokens)
            throw new ArgumentException("Le message et l’instruction système dépassent le budget de contexte. Raccourcissez-les.");
    }

    public int PromptCost(string system, string user) =>
        Count(user) + (system.Length == 0 ? 0 : Count(system));

    public int Count(string text) => PromptTokens.Count(text, Inference.TokenCounter);
}
