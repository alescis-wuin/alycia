using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Generations;

public sealed class GenerationContextBudgetTests
{
    [Fact]
    public void KnownContextAndOutputReservationProduceMaximumInputBudget()
    {
        GenerationContextBudget budget = new(
            contextWindowTokens: 8192,
            reservedOutputTokens: 768);

        Assert.Equal(8192, budget.ContextWindowTokens);
        Assert.Equal(768, budget.ReservedOutputTokens);
        Assert.Equal(7424, budget.MaximumInputTokens);
        Assert.True(budget.IsMaximumInputKnown);
    }

    [Fact]
    public void UnknownProviderDefaultsRemainUnknownInsteadOfBeingInvented()
    {
        GenerationContextBudget budget = new(
            contextWindowTokens: 4096,
            reservedOutputTokens: null);

        Assert.Equal(4096, budget.ContextWindowTokens);
        Assert.Null(budget.ReservedOutputTokens);
        Assert.Null(budget.MaximumInputTokens);
        Assert.False(budget.IsMaximumInputKnown);
    }

    [Fact]
    public void ReservationCannotConsumeEntireExplicitContextWindow()
    {
        Assert.Throws<ArgumentException>(() => new GenerationContextBudget(
            contextWindowTokens: 1024,
            reservedOutputTokens: 1024));
    }

    [Fact]
    public void VersionTwoSnapshotCapturesContextRevisionAndExplicitBudget()
    {
        ConversationContextRevisionId contextRevisionId = ConversationContextRevisionId.New();
        MessageRevisionId triggerRevisionId = MessageRevisionId.New();
        InferenceProviderConfiguration configuration = new(
            "provider.alpha",
            "owner/model",
            contextSize: 8192,
            generation: new InferenceGenerationOptions(maxOutputTokens: 512));
        GenerationContextBudget budget =
            GenerationContextBudget.FromConfiguration(configuration);

        GenerationSnapshot snapshot = new(
            GenerationSnapshotId.New(),
            ConversationId.New(),
            MessageId.New(),
            triggerRevisionId,
            new[] { triggerRevisionId },
            DateTimeOffset.UtcNow,
            configuration,
            profileId: null,
            profileRevisionId: null,
            contextRevisionId,
            budget);

        Assert.Equal(contextRevisionId, snapshot.ContextRevisionId);
        GenerationContextBudget capturedBudget =
            Assert.IsType<GenerationContextBudget>(snapshot.ContextBudget);
        Assert.Same(budget, capturedBudget);
        Assert.True(snapshot.HasExplicitContextBudget);
        Assert.Equal(7680, capturedBudget.MaximumInputTokens);
        Assert.Matches("^[0-9A-F]{64}$", snapshot.PayloadHash);
    }
}
