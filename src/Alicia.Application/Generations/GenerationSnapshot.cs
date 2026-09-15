using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public sealed record GenerationSnapshot
{
    public GenerationSnapshot(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        DateTimeOffset capturedAt,
        InferenceProviderConfiguration providerConfiguration)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (triggeringUserMessageId.IsEmpty)
        {
            throw new ArgumentException(
                "Triggering user-message identifier cannot be empty.",
                nameof(triggeringUserMessageId));
        }

        ArgumentNullException.ThrowIfNull(providerConfiguration);

        ConversationId = conversationId;
        TriggeringUserMessageId = triggeringUserMessageId;
        CapturedAtUtc = capturedAt.ToUniversalTime();
        ProviderId = providerConfiguration.ProviderId;
        ModelReference = providerConfiguration.ModelReference;
        ContextSize = providerConfiguration.ContextSize;
        GenerationOptions = CopyGenerationOptions(providerConfiguration.Generation);
    }

    public ConversationId ConversationId { get; }

    public MessageId TriggeringUserMessageId { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public string ProviderId { get; }

    public string? ModelReference { get; }

    public int? ContextSize { get; }

    public InferenceGenerationOptions GenerationOptions { get; }

    public bool UsesProviderDefaults => ContextSize is null
        && GenerationOptions.UsesOnlyProviderDefaults;

    private static InferenceGenerationOptions CopyGenerationOptions(
        InferenceGenerationOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new InferenceGenerationOptions(
            source.MaxOutputTokens,
            source.Temperature,
            source.TopP,
            source.TopK,
            source.Seed,
            source.ReasoningEnabled,
            source.ReasoningBudgetTokens);
    }
}
