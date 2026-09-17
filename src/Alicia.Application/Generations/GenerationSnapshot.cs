using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public sealed record GenerationSnapshot
{
    private const string PayloadHashSchema = "alicia-generation-snapshot-payload-v1";
    private readonly ReadOnlyCollection<MessageRevisionId> _inputMessageRevisionIds;

    public GenerationSnapshot(
        GenerationSnapshotId id,
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IEnumerable<MessageRevisionId> inputMessageRevisionIds,
        DateTimeOffset capturedAt,
        InferenceProviderConfiguration providerConfiguration,
        GenerationProfileId? profileId = null,
        GenerationProfileRevisionId? profileRevisionId = null)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-snapshot identifier cannot be empty.",
                nameof(id));
        }

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

        if (triggeringUserMessageRevisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Triggering user-message revision identifier cannot be empty.",
                nameof(triggeringUserMessageRevisionId));
        }

        ArgumentNullException.ThrowIfNull(inputMessageRevisionIds);
        MessageRevisionId[] revisions = inputMessageRevisionIds.ToArray();

        if (revisions.Length == 0)
        {
            throw new ArgumentException(
                "Generation snapshot must capture at least one input message revision.",
                nameof(inputMessageRevisionIds));
        }

        HashSet<MessageRevisionId> uniqueRevisions = [];
        foreach (MessageRevisionId revisionId in revisions)
        {
            if (revisionId.IsEmpty)
            {
                throw new ArgumentException(
                    "Input message revision identifiers cannot be empty.",
                    nameof(inputMessageRevisionIds));
            }

            if (!uniqueRevisions.Add(revisionId))
            {
                throw new ArgumentException(
                    "Input message revision identifiers must be unique.",
                    nameof(inputMessageRevisionIds));
            }
        }

        if (revisions[^1] != triggeringUserMessageRevisionId)
        {
            throw new ArgumentException(
                "The triggering user-message revision must be the latest captured input revision.",
                nameof(inputMessageRevisionIds));
        }

        if (capturedAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capturedAt),
                capturedAt,
                "Generation snapshot capture time must be defined.");
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

        Id = id;
        ConversationId = conversationId;
        TriggeringUserMessageId = triggeringUserMessageId;
        TriggeringUserMessageRevisionId = triggeringUserMessageRevisionId;
        _inputMessageRevisionIds = Array.AsReadOnly(revisions);
        CapturedAtUtc = capturedAt.ToUniversalTime();
        ProviderId = providerConfiguration.ProviderId;
        ModelReference = providerConfiguration.ModelReference;
        ContextSize = providerConfiguration.ContextSize;
        GenerationOptions = CopyGenerationOptions(providerConfiguration.Generation);
        ProfileId = profileId;
        ProfileRevisionId = profileRevisionId;
        PayloadHash = ComputePayloadHash();
    }

    public GenerationSnapshotId Id { get; }

    public ConversationId ConversationId { get; }

    public MessageId TriggeringUserMessageId { get; }

    public MessageRevisionId TriggeringUserMessageRevisionId { get; }

    public IReadOnlyList<MessageRevisionId> InputMessageRevisionIds => _inputMessageRevisionIds;

    public DateTimeOffset CapturedAtUtc { get; }

    public string ProviderId { get; }

    public string? ModelReference { get; }

    public int? ContextSize { get; }

    public InferenceGenerationOptions GenerationOptions { get; }

    public GenerationProfileId? ProfileId { get; }

    public GenerationProfileRevisionId? ProfileRevisionId { get; }

    public string PayloadHash { get; }

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

    private string ComputePayloadHash()
    {
        StringBuilder canonical = new();
        AppendCanonical(canonical, PayloadHashSchema);
        AppendCanonical(canonical, ConversationId.ToString());
        AppendCanonical(canonical, TriggeringUserMessageId.ToString());
        AppendCanonical(canonical, TriggeringUserMessageRevisionId.ToString());
        AppendCanonical(canonical, CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        AppendCanonical(canonical, ProviderId);
        AppendCanonical(canonical, ModelReference);
        AppendCanonical(canonical, FormatNullable(ContextSize));
        AppendCanonical(canonical, FormatNullable(GenerationOptions.MaxOutputTokens));
        AppendCanonical(canonical, FormatNullable(GenerationOptions.Temperature));
        AppendCanonical(canonical, FormatNullable(GenerationOptions.TopP));
        AppendCanonical(canonical, FormatNullable(GenerationOptions.TopK));
        AppendCanonical(canonical, FormatNullable(GenerationOptions.Seed));
        AppendCanonical(canonical, FormatNullable(GenerationOptions.ReasoningEnabled));
        AppendCanonical(canonical, FormatNullable(GenerationOptions.ReasoningBudgetTokens));
        AppendCanonical(canonical, ProfileId?.ToString());
        AppendCanonical(canonical, ProfileRevisionId?.ToString());
        AppendCanonical(
            canonical,
            InputMessageRevisionIds.Count.ToString(CultureInfo.InvariantCulture));

        foreach (MessageRevisionId revisionId in InputMessageRevisionIds)
        {
            AppendCanonical(canonical, revisionId.ToString());
        }

        byte[] payload = Encoding.UTF8.GetBytes(canonical.ToString());
        byte[] digest = SHA256.HashData(payload);
        return Convert.ToHexString(digest);
    }

    private static void AppendCanonical(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:;");
            return;
        }

        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');
    }

    private static string? FormatNullable(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static string? FormatNullable(double? value)
    {
        return value?.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string? FormatNullable(bool? value)
    {
        return value switch
        {
            true => "1",
            false => "0",
            null => null,
        };
    }
}
