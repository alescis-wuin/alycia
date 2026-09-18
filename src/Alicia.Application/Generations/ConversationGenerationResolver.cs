using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public sealed class ConversationGenerationResolver : IConversationBranchContextGenerationResolver
{
    private readonly IConversationGenerationSelectionStore _selectionStore;
    private readonly IGenerationProfileCatalogStore _profileCatalogStore;
    private readonly IInferenceProviderConfigurationStore _providerConfigurationStore;
    private readonly TimeProvider _timeProvider;

    public ConversationGenerationResolver(
        IConversationGenerationSelectionStore selectionStore,
        IGenerationProfileCatalogStore profileCatalogStore,
        IInferenceProviderConfigurationStore providerConfigurationStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(selectionStore);
        ArgumentNullException.ThrowIfNull(profileCatalogStore);
        ArgumentNullException.ThrowIfNull(providerConfigurationStore);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _selectionStore = selectionStore;
        _profileCatalogStore = profileCatalogStore;
        _providerConfigurationStore = providerConfigurationStore;
        _timeProvider = timeProvider;
    }

    public Task<ResolvedConversationGeneration?> ResolveAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(
            conversationId,
            triggeringUserMessageId,
            triggeringUserMessageRevisionId,
            inputMessageRevisionIds,
            contextRevision: null,
            cancellationToken: cancellationToken);
    }

    public Task<ResolvedConversationGeneration?> ResolveAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        ConversationContextRevision? contextRevision,
        CancellationToken cancellationToken = default)
    {
        return ResolveCoreAsync(
            conversationId,
            null,
            triggeringUserMessageId,
            triggeringUserMessageRevisionId,
            inputMessageRevisionIds,
            contextRevision,
            cancellationToken);
    }

    public Task<ResolvedConversationGeneration?> ResolveAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        ConversationContextRevision? contextRevision,
        CancellationToken cancellationToken = default)
    {
        if (branchId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation branch identifier cannot be empty.",
                nameof(branchId));
        }

        return ResolveCoreAsync(
            conversationId,
            branchId,
            triggeringUserMessageId,
            triggeringUserMessageRevisionId,
            inputMessageRevisionIds,
            contextRevision,
            cancellationToken);
    }

    private async Task<ResolvedConversationGeneration?> ResolveCoreAsync(
        ConversationId conversationId,
        ConversationBranchId? branchId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        ConversationContextRevision? contextRevision,
        CancellationToken cancellationToken)
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

        if (triggeringUserMessageRevisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Triggering user-message revision identifier cannot be empty.",
                nameof(triggeringUserMessageRevisionId));
        }

        ArgumentNullException.ThrowIfNull(inputMessageRevisionIds);
        ValidateContextRevision(conversationId, contextRevision);

        ConversationGenerationSelection? selection =
            branchId is ConversationBranchId scopedBranchId
            && _selectionStore is IConversationBranchGenerationSelectionStore branchSelectionStore
                ? await branchSelectionStore
                    .LoadAsync(conversationId, scopedBranchId, cancellationToken)
                    .ConfigureAwait(false)
                : await _selectionStore
                    .LoadAsync(conversationId, cancellationToken)
                    .ConfigureAwait(false);
        if (selection is null)
        {
            return await ResolveLegacySelectionAsync(
                    conversationId,
                    triggeringUserMessageId,
                    triggeringUserMessageRevisionId,
                    inputMessageRevisionIds,
                    contextRevision,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        GenerationProfileModelScope scope = selection.ModelScope;
        InferenceProviderConfiguration providerConfiguration = await _providerConfigurationStore
            .LoadAsync(scope.ProviderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Conversation generation selection references provider '{scope.ProviderId}', but no saved provider configuration exists.");

        if (!string.Equals(
            providerConfiguration.ModelReference,
            scope.ModelReference,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The conversation-selected model does not match the saved provider model. Load the selected model before generating a response.");
        }

        GenerationProfileCatalog catalog = await _profileCatalogStore
            .LoadAsync(scope, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No generation-profile catalog exists for model scope '{scope}'.");
        GenerationProfile profile = catalog.FindProfile(selection.ProfileId)
            ?? throw new InvalidOperationException(
                $"The selected generation profile '{selection.ProfileId}' does not exist in model scope '{scope}'.");
        GenerationProfileRevisionId? revisionId = null;

        if (!profile.IsDefault)
        {
            GenerationProfileRevision revision = catalog.FindLatestRevision(profile.Id)
                ?? throw new InvalidOperationException(
                    $"The selected generation profile '{profile.Id}' has no confirmed revision.");
            profile = revision.Profile;
            revisionId = revision.Id;
        }

        InferenceProviderConfiguration resolvedConfiguration = new(
            scope.ProviderId,
            scope.ModelReference,
            providerConfiguration.ContextSize,
            profile.GenerationOptions);
        GenerationSnapshot snapshot = CreateSnapshot(
            conversationId,
            triggeringUserMessageId,
            triggeringUserMessageRevisionId,
            inputMessageRevisionIds,
            resolvedConfiguration,
            profile.Id,
            revisionId,
            contextRevision);

        return new ResolvedConversationGeneration(
            snapshot,
            ComposeSystemInstructions(
                profile.BaseSystemInstructions,
                contextRevision));
    }

    private async Task<ResolvedConversationGeneration?> ResolveLegacySelectionAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        ConversationContextRevision? contextRevision,
        CancellationToken cancellationToken)
    {
        string? providerId = await _providerConfigurationStore
            .LoadSelectedProviderIdAsync(cancellationToken)
            .ConfigureAwait(false);
        if (providerId is null)
        {
            return null;
        }

        InferenceProviderConfiguration providerConfiguration = await _providerConfigurationStore
            .LoadAsync(providerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Selected provider '{providerId}' does not have a saved configuration.");
        GenerationSnapshot snapshot = CreateSnapshot(
            conversationId,
            triggeringUserMessageId,
            triggeringUserMessageRevisionId,
            inputMessageRevisionIds,
            providerConfiguration,
            profileId: null,
            profileRevisionId: null,
            contextRevision);

        return new ResolvedConversationGeneration(
            snapshot,
            ComposeSystemInstructions(profileInstructions: null, contextRevision));
    }

    private GenerationSnapshot CreateSnapshot(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        InferenceProviderConfiguration providerConfiguration,
        GenerationProfileId? profileId,
        GenerationProfileRevisionId? profileRevisionId,
        ConversationContextRevision? contextRevision)
    {
        GenerationContextBudget budget =
            GenerationContextBudget.FromConfiguration(providerConfiguration);

        return new GenerationSnapshot(
            GenerationSnapshotId.New(),
            conversationId,
            triggeringUserMessageId,
            triggeringUserMessageRevisionId,
            inputMessageRevisionIds,
            _timeProvider.GetUtcNow(),
            providerConfiguration,
            profileId,
            profileRevisionId,
            contextRevision?.RevisionId,
            budget);
    }

    private static void ValidateContextRevision(
        ConversationId conversationId,
        ConversationContextRevision? contextRevision)
    {
        if (contextRevision is null)
        {
            return;
        }

        ConversationContextId expectedContextId = new(conversationId.Value);
        if (contextRevision.ContextId != expectedContextId)
        {
            throw new ArgumentException(
                "Conversation-context revision does not belong to the generation conversation.",
                nameof(contextRevision));
        }
    }

    private static string? ComposeSystemInstructions(
        string? profileInstructions,
        ConversationContextRevision? contextRevision)
    {
        if (contextRevision is null)
        {
            return NormalizeOptionalInstructions(profileInstructions);
        }

        string? contextInstructions =
            NormalizeOptionalInstructions(contextRevision.Instructions);
        if (contextRevision.ReplaceProfileInstructions)
        {
            return contextInstructions;
        }

        string? normalizedProfile = NormalizeOptionalInstructions(profileInstructions);
        if (normalizedProfile is null)
        {
            return contextInstructions;
        }

        if (contextInstructions is null)
        {
            return normalizedProfile;
        }

        return normalizedProfile + "\n\n" + contextInstructions;
    }

    private static string? NormalizeOptionalInstructions(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
