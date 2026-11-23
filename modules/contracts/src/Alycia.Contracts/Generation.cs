namespace Alycia.Contracts.Chat;

public sealed record GenerationStats(int? PromptTokens, int? OutputTokens, long? TotalNanoseconds,
    long? EvaluationNanoseconds, string? DoneReason, long? PromptEvaluationNanoseconds = null, long? LoadNanoseconds = null, int? CachedPromptTokens = null);
public sealed record ModelCallUsage(string Purpose, int EstimatedInputTokens, double ElapsedMilliseconds, GenerationStats? Stats, double? FirstContentMilliseconds = null, int? EffectiveContextTokens = null, long? ApiWorkingSetBytes = null, long? ModelVramBytes = null);
public sealed record GenerationUsage(IReadOnlyList<ModelCallUsage> Calls, string? ModelIdentity, string Counter,
    long? InputTokens, long? OutputTokens, double ElapsedMilliseconds, bool Complete);
