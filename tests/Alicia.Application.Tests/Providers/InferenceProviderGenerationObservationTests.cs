using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Providers;

public sealed class InferenceProviderGenerationObservationTests
{
    [Fact]
    public void ObservationCapturesProviderNeutralGenerationMetrics()
    {
        DateTimeOffset started = new(2026, 8, 16, 19, 0, 0, TimeSpan.Zero);
        InferenceProviderGenerationObservation observation = new(
            providerId: "llama.cpp.cuda",
            providerName: "llama.cpp CUDA",
            modelReference: "Qwen/Qwen3-4B-GGUF",
            runtimeVersion: "b10435",
            startedAtUtc: started,
            completedAtUtc: started.AddSeconds(2),
            outcome: InferenceProviderGenerationOutcome.Completed,
            failureKind: null,
            duration: TimeSpan.FromSeconds(2),
            timeToFirstOutput: TimeSpan.FromMilliseconds(250),
            inputTokens: 120,
            outputTokens: 30,
            totalTokens: 150,
            cachedInputTokens: 80,
            promptEvaluationDuration: TimeSpan.FromMilliseconds(40),
            generationDuration: TimeSpan.FromMilliseconds(800),
            promptTokensPerSecond: 3000,
            generationTokensPerSecond: 37.5);

        Assert.Equal(InferenceProviderGenerationOutcome.Completed, observation.Outcome);
        Assert.Equal("Qwen/Qwen3-4B-GGUF", observation.ModelReference);
        Assert.Equal(120, observation.InputTokens);
        Assert.Equal(30, observation.OutputTokens);
        Assert.Equal(80, observation.CachedInputTokens);
        Assert.Null(observation.FailureKind);
    }

    [Fact]
    public void FailedObservationRequiresFailureClassification()
    {
        Assert.Throws<ArgumentException>(() => CreateObservation(
            outcome: InferenceProviderGenerationOutcome.Failed,
            failureKind: null));
    }

    [Fact]
    public void CompletedObservationRejectsFailureClassification()
    {
        Assert.Throws<ArgumentException>(() => CreateObservation(
            outcome: InferenceProviderGenerationOutcome.Completed,
            failureKind: InferenceProviderFailureKind.Network));
    }

    [Fact]
    public void ObservationRejectsInconsistentTokenTotals()
    {
        Assert.Throws<ArgumentException>(() => new InferenceProviderGenerationObservation(
            providerId: "provider",
            providerName: "Provider",
            modelReference: null,
            runtimeVersion: null,
            startedAtUtc: DateTimeOffset.UnixEpoch,
            completedAtUtc: DateTimeOffset.UnixEpoch,
            outcome: InferenceProviderGenerationOutcome.Completed,
            failureKind: null,
            duration: TimeSpan.Zero,
            timeToFirstOutput: null,
            inputTokens: 10,
            outputTokens: 5,
            totalTokens: 99,
            cachedInputTokens: 0,
            promptEvaluationDuration: null,
            generationDuration: null,
            promptTokensPerSecond: null,
            generationTokensPerSecond: null));
    }

    [Fact]
    public void ObservationRejectsNegativeTokenMetrics()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InferenceProviderGenerationObservation(
            providerId: "provider",
            providerName: "Provider",
            modelReference: null,
            runtimeVersion: null,
            startedAtUtc: DateTimeOffset.UnixEpoch,
            completedAtUtc: DateTimeOffset.UnixEpoch,
            outcome: InferenceProviderGenerationOutcome.Completed,
            failureKind: null,
            duration: TimeSpan.Zero,
            timeToFirstOutput: null,
            inputTokens: -1,
            outputTokens: null,
            totalTokens: null,
            cachedInputTokens: null,
            promptEvaluationDuration: null,
            generationDuration: null,
            promptTokensPerSecond: null,
            generationTokensPerSecond: null));
    }

    private static InferenceProviderGenerationObservation CreateObservation(
        InferenceProviderGenerationOutcome outcome,
        InferenceProviderFailureKind? failureKind)
    {
        return new InferenceProviderGenerationObservation(
            providerId: "provider",
            providerName: "Provider",
            modelReference: null,
            runtimeVersion: null,
            startedAtUtc: DateTimeOffset.UnixEpoch,
            completedAtUtc: DateTimeOffset.UnixEpoch,
            outcome,
            failureKind,
            duration: TimeSpan.Zero,
            timeToFirstOutput: null,
            inputTokens: null,
            outputTokens: null,
            totalTokens: null,
            cachedInputTokens: null,
            promptEvaluationDuration: null,
            generationDuration: null,
            promptTokensPerSecond: null,
            generationTokensPerSecond: null);
    }
}
