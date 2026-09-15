namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed record LlamaCppGenerationMetrics(
    int? InputTokens = null,
    int? OutputTokens = null,
    int? TotalTokens = null,
    int? CachedInputTokens = null,
    TimeSpan? PromptEvaluationDuration = null,
    TimeSpan? GenerationDuration = null,
    double? PromptTokensPerSecond = null,
    double? GenerationTokensPerSecond = null)
{
    public LlamaCppGenerationMetrics Merge(LlamaCppGenerationMetrics newer)
    {
        ArgumentNullException.ThrowIfNull(newer);

        return new LlamaCppGenerationMetrics(
            newer.InputTokens ?? InputTokens,
            newer.OutputTokens ?? OutputTokens,
            newer.TotalTokens ?? TotalTokens,
            newer.CachedInputTokens ?? CachedInputTokens,
            newer.PromptEvaluationDuration ?? PromptEvaluationDuration,
            newer.GenerationDuration ?? GenerationDuration,
            newer.PromptTokensPerSecond ?? PromptTokensPerSecond,
            newer.GenerationTokensPerSecond ?? GenerationTokensPerSecond);
    }
}
