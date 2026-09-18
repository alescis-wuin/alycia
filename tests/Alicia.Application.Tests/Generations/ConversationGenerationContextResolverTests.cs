using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Generations;

public sealed class ConversationGenerationContextResolverTests
{
    [Fact]
    public async Task ResolveAsyncExtendsProfileInstructionsAndCapturesContextProvenance()
    {
        ConversationId conversationId = ConversationId.New();
        MessageId userMessageId = MessageId.New();
        MessageRevisionId userRevisionId = MessageRevisionId.New();
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfileId defaultProfileId = GenerationProfileId.New();
        GenerationProfileId profileId = GenerationProfileId.New();
        GenerationProfileRevision profileRevision = new(
            GenerationProfileRevisionId.New(),
            parentRevisionId: null,
            new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero),
            new GenerationProfile(
                profileId,
                "Code",
                "Profile instructions",
                new InferenceGenerationOptions(maxOutputTokens: 512)));
        GenerationProfileCatalog catalog = new(
            scope,
            GenerationProfile.CreateDefault(defaultProfileId),
            new[] { profileRevision });
        ConversationContextRevision contextRevision = new(
            new ConversationContextId(conversationId.Value),
            ConversationContextRevisionId.New(),
            parentRevisionId: null,
            "Conversation context",
            replaceProfileInstructions: false,
            new DateTimeOffset(2026, 9, 18, 9, 30, 0, TimeSpan.Zero));
        DateTimeOffset capturedAt =
            new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        ConversationGenerationResolver resolver = new(
            new SelectionStore(new ConversationGenerationSelection(
                conversationId,
                scope,
                profileId)),
            new CatalogStore(catalog),
            new ProviderStore(new InferenceProviderConfiguration(
                scope.ProviderId,
                scope.ModelReference,
                contextSize: 8192)),
            new FixedTimeProvider(capturedAt));

        ResolvedConversationGeneration? result = await resolver.ResolveAsync(
            conversationId,
            userMessageId,
            userRevisionId,
            new[] { userRevisionId },
            contextRevision,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        ResolvedConversationGeneration resolved =
            Assert.IsType<ResolvedConversationGeneration>(result);
        Assert.Equal("Profile instructions\n\nConversation context", resolved.SystemInstructions);
        Assert.Equal(contextRevision.RevisionId, resolved.Snapshot.ContextRevisionId);
        GenerationContextBudget budget =
            Assert.IsType<GenerationContextBudget>(resolved.Snapshot.ContextBudget);
        Assert.Equal(8192, budget.ContextWindowTokens);
        Assert.Equal(512, budget.ReservedOutputTokens);
        Assert.Equal(7680, budget.MaximumInputTokens);
        Assert.Equal(profileRevision.Id, resolved.Snapshot.ProfileRevisionId);
        Assert.Equal(capturedAt, resolved.Snapshot.CapturedAtUtc);
    }

    [Fact]
    public async Task ResolveAsyncCanReplaceProfileInstructionsWithoutInventingUnknownOutputBudget()
    {
        ConversationId conversationId = ConversationId.New();
        MessageRevisionId userRevisionId = MessageRevisionId.New();
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfileId defaultProfileId = GenerationProfileId.New();
        GenerationProfileId profileId = GenerationProfileId.New();
        GenerationProfileRevision profileRevision = new(
            GenerationProfileRevisionId.New(),
            null,
            DateTimeOffset.UtcNow,
            new GenerationProfile(
                profileId,
                "General",
                "Base instructions"));
        ConversationContextRevision contextRevision = new(
            new ConversationContextId(conversationId.Value),
            ConversationContextRevisionId.New(),
            null,
            "Context replaces profile",
            replaceProfileInstructions: true,
            DateTimeOffset.UtcNow);
        ConversationGenerationResolver resolver = new(
            new SelectionStore(new ConversationGenerationSelection(
                conversationId,
                scope,
                profileId)),
            new CatalogStore(new GenerationProfileCatalog(
                scope,
                GenerationProfile.CreateDefault(defaultProfileId),
                new[] { profileRevision })),
            new ProviderStore(new InferenceProviderConfiguration(
                scope.ProviderId,
                scope.ModelReference,
                contextSize: 4096)),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        ResolvedConversationGeneration? result = await resolver.ResolveAsync(
            conversationId,
            MessageId.New(),
            userRevisionId,
            new[] { userRevisionId },
            contextRevision,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        ResolvedConversationGeneration resolved =
            Assert.IsType<ResolvedConversationGeneration>(result);
        Assert.Equal("Context replaces profile", resolved.SystemInstructions);
        GenerationContextBudget budget =
            Assert.IsType<GenerationContextBudget>(resolved.Snapshot.ContextBudget);
        Assert.Equal(4096, budget.ContextWindowTokens);
        Assert.Null(budget.ReservedOutputTokens);
        Assert.Null(budget.MaximumInputTokens);
    }

    private sealed class SelectionStore : IConversationGenerationSelectionStore
    {
        private readonly ConversationGenerationSelection _selection;

        public SelectionStore(ConversationGenerationSelection selection)
        {
            _selection = selection;
        }

        public Task<ConversationGenerationSelection?> LoadAsync(
            ConversationId conversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ConversationGenerationSelection?>(
                conversationId == _selection.ConversationId ? _selection : null);
        }

        public Task SaveAsync(
            ConversationGenerationSelection selection,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(
            ConversationId conversationId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CatalogStore : IGenerationProfileCatalogStore
    {
        private readonly GenerationProfileCatalog _catalog;

        public CatalogStore(GenerationProfileCatalog catalog)
        {
            _catalog = catalog;
        }

        public Task<GenerationProfileCatalog?> LoadAsync(
            GenerationProfileModelScope scope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<GenerationProfileCatalog?>(
                scope == _catalog.Scope ? _catalog : null);
        }

        public Task SaveAsync(
            GenerationProfileCatalog catalog,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ProviderStore : IInferenceProviderConfigurationStore
    {
        private readonly InferenceProviderConfiguration _configuration;

        public ProviderStore(InferenceProviderConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<string?> LoadSelectedProviderIdAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(_configuration.ProviderId);
        }

        public Task<InferenceProviderConfiguration?> LoadAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<InferenceProviderConfiguration?>(
                string.Equals(providerId, _configuration.ProviderId, StringComparison.Ordinal)
                    ? _configuration
                    : null);
        }

        public Task SaveAsync(
            InferenceProviderConfiguration configuration,
            bool selectProvider,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
