using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Context;

public sealed record SummaryParameters(int ContextTokens, int SafetyTokens, int MaxCalls, int MaxDepth,
    int? Seed, int TimeoutSeconds, string TokenCounting, string? ModelIdentity, ITextTokenCounter? TokenCounter)
{
    public InferenceCall Generation(int outputTokens) => new(
        new(ContextTokens, outputTokens, false, 0, Seed, TimeoutSeconds, "summary", null),
        SafetyTokens, false, TokenCounter);
}
