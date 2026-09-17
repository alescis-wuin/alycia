using System.Reflection;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Generations;

public sealed class GenerationSnapshotTests
{
    [Fact]
    public void SnapshotCapturesProviderAndInputRevisionStateDefensively()
    {
        ConversationId conversationId = ConversationId.New();
        MessageId triggeringUserMessageId = MessageId.New();
        MessageRevisionId firstRevisionId = MessageRevisionId.New();
        MessageRevisionId triggeringRevisionId = MessageRevisionId.New();
        List<MessageRevisionId> inputRevisionIds =
        [
            firstRevisionId,
            triggeringRevisionId,
        ];
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
        GenerationSnapshotId snapshotId = GenerationSnapshotId.New();

        GenerationSnapshot snapshot = new(
            snapshotId,
            conversationId,
            triggeringUserMessageId,
            triggeringRevisionId,
            inputRevisionIds,
            capturedAt,
            configuration);
        inputRevisionIds[0] = MessageRevisionId.New();

        Assert.Equal(snapshotId, snapshot.Id);
        Assert.Equal(conversationId, snapshot.ConversationId);
        Assert.Equal(triggeringUserMessageId, snapshot.TriggeringUserMessageId);
        Assert.Equal(triggeringRevisionId, snapshot.TriggeringUserMessageRevisionId);
        Assert.Equal(
            new[] { firstRevisionId, triggeringRevisionId },
            snapshot.InputMessageRevisionIds);
        Assert.Equal(capturedAt.ToUniversalTime(), snapshot.CapturedAtUtc);
        Assert.Equal("provider.alpha", snapshot.ProviderId);
        Assert.Equal("owner/model:Q4_K_M", snapshot.ModelReference);
        Assert.Equal(8192, snapshot.ContextSize);
        Assert.Equal(options, snapshot.GenerationOptions);
        Assert.NotSame(options, snapshot.GenerationOptions);
        Assert.Matches("^[0-9A-F]{64}$", snapshot.PayloadHash);
        Assert.False(snapshot.UsesProviderDefaults);
    }

    [Fact]
    public void SnapshotPreservesProviderDefaultSemantics()
    {
        MessageRevisionId revisionId = MessageRevisionId.New();
        InferenceProviderConfiguration configuration = new(
            "provider.alpha",
            "owner/model");

        GenerationSnapshot snapshot = new(
            GenerationSnapshotId.New(),
            ConversationId.New(),
            MessageId.New(),
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            configuration);

        Assert.Equal("owner/model", snapshot.ModelReference);
        Assert.Null(snapshot.ContextSize);
        Assert.True(snapshot.GenerationOptions.UsesOnlyProviderDefaults);
        Assert.True(snapshot.UsesProviderDefaults);
        Assert.False(snapshot.HasProfileSelection);
    }

    [Fact]
    public void SnapshotRejectsInvalidCorrelationInputs()
    {
        GenerationSnapshotId snapshotId = GenerationSnapshotId.New();
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId revisionId = MessageRevisionId.New();
        InferenceProviderConfiguration configuration = new("provider.alpha");

        Assert.Throws<ArgumentException>(() => new GenerationSnapshot(
            default,
            conversationId,
            messageId,
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            configuration));
        Assert.Throws<ArgumentException>(() => new GenerationSnapshot(
            snapshotId,
            default,
            messageId,
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            configuration));
        Assert.Throws<ArgumentException>(() => new GenerationSnapshot(
            snapshotId,
            conversationId,
            default,
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            configuration));
        Assert.Throws<ArgumentException>(() => new GenerationSnapshot(
            snapshotId,
            conversationId,
            messageId,
            default,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            configuration));
        Assert.Throws<ArgumentException>(() => new GenerationSnapshot(
            snapshotId,
            conversationId,
            messageId,
            revisionId,
            Array.Empty<MessageRevisionId>(),
            DateTimeOffset.UtcNow,
            configuration));
        Assert.Throws<ArgumentException>(() => new GenerationSnapshot(
            snapshotId,
            conversationId,
            messageId,
            revisionId,
            new[] { MessageRevisionId.New() },
            DateTimeOffset.UtcNow,
            configuration));
        Assert.Throws<ArgumentNullException>(() => new GenerationSnapshot(
            snapshotId,
            conversationId,
            messageId,
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            null!));
    }

    [Fact]
    public void PayloadHashIsDeterministicAndInputOrderSensitive()
    {
        GenerationSnapshotId firstId = GenerationSnapshotId.New();
        GenerationSnapshotId secondId = GenerationSnapshotId.New();
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId firstRevisionId = MessageRevisionId.New();
        MessageRevisionId triggerRevisionId = MessageRevisionId.New();
        DateTimeOffset capturedAt = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
        InferenceProviderConfiguration configuration = new(
            "provider.alpha",
            "owner/model",
            4096,
            new InferenceGenerationOptions(temperature: 0.4));
        GenerationSnapshot first = new(
            firstId,
            conversationId,
            messageId,
            triggerRevisionId,
            new[] { firstRevisionId, triggerRevisionId },
            capturedAt,
            configuration);
        GenerationSnapshot second = new(
            secondId,
            conversationId,
            messageId,
            triggerRevisionId,
            new[] { firstRevisionId, triggerRevisionId },
            capturedAt,
            configuration);
        GenerationSnapshot changed = new(
            GenerationSnapshotId.New(),
            conversationId,
            messageId,
            triggerRevisionId,
            new[] { MessageRevisionId.New(), triggerRevisionId },
            capturedAt,
            configuration);

        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.NotEqual(first.PayloadHash, changed.PayloadHash);
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
