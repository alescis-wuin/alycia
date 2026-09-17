using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public sealed record GenerationSnapshot
{
    public GenerationSnapshot(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        DateTimeOffset capturedAt,
        InferenceProviderConfiguration providerConfiguration,
        GenerationProfileId? profileId = null,
        GenerationProfileRevisionId? profileRevisionId = null)
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

        if (profileId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Generation-profile identifier cannot be empty when supplied.",
                nameof(profileId));
        }

        if (profileRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Generation-profile revision identifier cannot be empty when supplied.",
                nameof(profileRevisionId));
        }

        if (profileRevisionId is not null && profileId is null)
        {
            throw new ArgumentException(
                "A generation-profile revision requires a generation-profile identifier.",
                nameof(profileRevisionId));
        }

        ConversationId = conversationId;
        TriggeringUserMessageId = triggeringUserMessageId;
        CapturedAtUtc = capturedAt.ToUniversalTime();
        ProviderId = providerConfiguration.ProviderId;
        ModelReference = providerConfiguration.ModelReference;
        ContextSize = providerConfiguration.ContextSize;
        GenerationOptions = CopyGenerationOptions(providerConfiguration.Generation);
        ProfileId = profileId;
        ProfileRevisionId = profileRevisionId;
    }

    public ConversationId ConversationId { get; }

    public MessageId TriggeringUserMessageId { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public string ProviderId { get; }

    public string? ModelReference { get; }

    public int? ContextSize { get; }

    public InferenceGenerationOptions GenerationOptions { get; }

    public GenerationProfileId? ProfileId { get; }

    public GenerationProfileRevisionId? ProfileRevisionId { get; }

    public bool HasProfileSelection => ProfileId is not null;

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
