using System.Reflection;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Generations;

public sealed class GenerationSnapshotTests
{
    [Fact]
    public void SnapshotCapturesProviderNeutralGenerationStateDefensively()
    {
        ConversationId conversationId = ConversationId.New();
        MessageId triggeringUserMessageId = MessageId.New();
        DateTimeOffset capturedAt =
            new(2026, 9, 14, 13, 30, 0, TimeSpan.FromHours(2));
        InferenceGenerationOptions options = new(
            maxOutputTokens: 512,
            temperature: 0.4,
            topP: 0.9,
            topK: 40,
            seed: 7,
            reasoningEnabled: true,
            reasoningBudgetTokens: 128);
        InferenceProviderConfiguration configuration = new(
            " provider.alpha ",
            " owner/model:Q4_K_M ",
            contextSize: 8192,
            generation: options);

        GenerationSnapshot snapshot = new(
            conversationId,
            triggeringUserMessageId,
            capturedAt,
            configuration);

        Assert.Equal(conversationId, snapshot.ConversationId);
        Assert.Equal(triggeringUserMessageId, snapshot.TriggeringUserMessageId);
        Assert.Equal(capturedAt.ToUniversalTime(), snapshot.CapturedAtUtc);
        Assert.Equal("provider.alpha", snapshot.ProviderId);
        Assert.Equal("owner/model:Q4_K_M", snapshot.ModelReference);
        Assert.Equal(8192, snapshot.ContextSize);
        Assert.Equal(options, snapshot.GenerationOptions);
        Assert.NotSame(options, snapshot.GenerationOptions);
        Assert.False(snapshot.UsesProviderDefaults);
    }

    [Fact]
    public void SnapshotPreservesProviderDefaultSemantics()
    {
        InferenceProviderConfiguration configuration = new(
            "provider.alpha",
            "owner/model");

        GenerationSnapshot snapshot = new(
            ConversationId.New(),
            MessageId.New(),
            DateTimeOffset.UtcNow,
            configuration);

        Assert.Equal("owner/model", snapshot.ModelReference);
        Assert.Null(snapshot.ContextSize);
        Assert.True(snapshot.GenerationOptions.UsesOnlyProviderDefaults);
        Assert.True(snapshot.UsesProviderDefaults);
    }

    [Fact]
    public void SnapshotRejectsInvalidCorrelationAndNullConfiguration()
    {
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        InferenceProviderConfiguration configuration = new("provider.alpha");

        Assert.Throws<ArgumentException>(() =>
            new GenerationSnapshot(
                default,
                messageId,
                DateTimeOffset.UtcNow,
                configuration));
        Assert.Throws<ArgumentException>(() =>
            new GenerationSnapshot(
                conversationId,
                default,
                DateTimeOffset.UtcNow,
                configuration));
        Assert.Throws<ArgumentNullException>(() =>
            new GenerationSnapshot(
                conversationId,
                messageId,
                DateTimeOffset.UtcNow,
                null!));
    }

    [Fact]
    public void SnapshotExposesNoPubliclyWritableState()
    {
        PropertyInfo[] properties = typeof(GenerationSnapshot)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.NotEmpty(properties);
        Assert.All(
            properties,
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }
}
