namespace Alicia.Application.Providers;

public sealed record InferenceProviderGenerationObservation
{
    public InferenceProviderGenerationObservation(
        string providerId,
        string providerName,
        string? modelReference,
        string? runtimeVersion,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        InferenceProviderGenerationOutcome outcome,
        InferenceProviderFailureKind? failureKind,
        TimeSpan duration,
        TimeSpan? timeToFirstOutput,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        int? cachedInputTokens,
        TimeSpan? promptEvaluationDuration,
        TimeSpan? generationDuration,
        double? promptTokensPerSecond,
        double? generationTokensPerSecond)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (failureKind is not null && !Enum.IsDefined(failureKind.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        if (outcome == InferenceProviderGenerationOutcome.Failed && failureKind is null)
        {
            throw new ArgumentException(
                "Failed provider observations require a failure classification.",
                nameof(failureKind));
        }

        if (outcome != InferenceProviderGenerationOutcome.Failed && failureKind is not null)
        {
            throw new ArgumentException(
                "Only failed provider observations can carry a failure classification.",
                nameof(failureKind));
        }

        if (completedAtUtc < startedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "Generation completion cannot precede generation start.");
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Generation duration cannot be negative.");
        }

        ValidateOptionalDuration(timeToFirstOutput, nameof(timeToFirstOutput));
        ValidateOptionalCount(inputTokens, nameof(inputTokens));
        ValidateOptionalCount(outputTokens, nameof(outputTokens));
        ValidateOptionalCount(totalTokens, nameof(totalTokens));
        ValidateOptionalCount(cachedInputTokens, nameof(cachedInputTokens));
        ValidateOptionalDuration(promptEvaluationDuration, nameof(promptEvaluationDuration));
        ValidateOptionalDuration(generationDuration, nameof(generationDuration));
        ValidateOptionalRate(promptTokensPerSecond, nameof(promptTokensPerSecond));
        ValidateOptionalRate(generationTokensPerSecond, nameof(generationTokensPerSecond));

        if (inputTokens is int input
            && outputTokens is int output
            && totalTokens is int total
            && total != input + output)
        {
            throw new ArgumentException(
                "Total token count must equal input plus output tokens when all values are available.",
                nameof(totalTokens));
        }

        if (cachedInputTokens is int cached
            && inputTokens is int totalInput
            && cached > totalInput)
        {
            throw new ArgumentException(
                "Cached input tokens cannot exceed total input tokens.",
                nameof(cachedInputTokens));
        }

        ProviderId = providerId.Trim();
        ProviderName = providerName.Trim();
        ModelReference = NormalizeOptional(modelReference);
        RuntimeVersion = NormalizeOptional(runtimeVersion);
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Outcome = outcome;
        FailureKind = failureKind;
        Duration = duration;
        TimeToFirstOutput = timeToFirstOutput;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
        CachedInputTokens = cachedInputTokens;
        PromptEvaluationDuration = promptEvaluationDuration;
        GenerationDuration = generationDuration;
        PromptTokensPerSecond = promptTokensPerSecond;
        GenerationTokensPerSecond = generationTokensPerSecond;
    }

    public string ProviderId { get; }

    public string ProviderName { get; }

    public string? ModelReference { get; }

    public string? RuntimeVersion { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public InferenceProviderGenerationOutcome Outcome { get; }

    public InferenceProviderFailureKind? FailureKind { get; }

    public TimeSpan Duration { get; }

    public TimeSpan? TimeToFirstOutput { get; }

    public int? InputTokens { get; }

    public int? OutputTokens { get; }

    public int? TotalTokens { get; }

    public int? CachedInputTokens { get; }

    public TimeSpan? PromptEvaluationDuration { get; }

    public TimeSpan? GenerationDuration { get; }

    public double? PromptTokensPerSecond { get; }

    public double? GenerationTokensPerSecond { get; }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void ValidateOptionalCount(int? value, string parameterName)
    {
        if (value is int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count, parameterName);
        }
    }

    private static void ValidateOptionalDuration(TimeSpan? value, string parameterName)
    {
        if (value is TimeSpan duration && duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Provider observation durations cannot be negative.");
        }
    }

    private static void ValidateOptionalRate(double? value, string parameterName)
    {
        if (value is not double rate)
        {
            return;
        }

        if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Provider observation rates must be finite and non-negative.");
        }
    }
}
