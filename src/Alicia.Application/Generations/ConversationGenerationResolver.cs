using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public sealed class ConversationGenerationResolver : IConversationGenerationResolver
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

    public async Task<ResolvedConversationGeneration?> ResolveAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        CancellationToken cancellationToken = default)
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

        ConversationGenerationSelection? selection = await _selectionStore
            .LoadAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        if (selection is null)
        {
            return null;
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
        GenerationSnapshot snapshot = new(
            conversationId,
            triggeringUserMessageId,
            _timeProvider.GetUtcNow(),
            resolvedConfiguration,
            profile.Id,
            revisionId);

        return new ResolvedConversationGeneration(
            snapshot,
            profile.BaseSystemInstructions);
    }
}
