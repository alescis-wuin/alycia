using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Generations;

public sealed class ConversationGenerationResolverTests
{
    [Fact]
    public async Task ResolveAsyncBindsLatestConfirmedCustomProfileRevision()
    {
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId earlierRevisionId = MessageRevisionId.New();
        MessageRevisionId triggerRevisionId = MessageRevisionId.New();
        MessageRevisionId[] inputRevisionIds = [earlierRevisionId, triggerRevisionId];
        GenerationProfileModelScope scope = new(
            "provider.alpha",
            "owner/model:Q4_K_M");
        GenerationProfileId defaultProfileId = GenerationProfileId.New();
        GenerationProfileId customProfileId = GenerationProfileId.New();
        GenerationProfileRevisionId firstRevisionId = GenerationProfileRevisionId.New();
        GenerationProfileRevisionId latestRevisionId = GenerationProfileRevisionId.New();
        GenerationProfileRevision firstRevision = new(
            firstRevisionId,
            parentRevisionId: null,
            new DateTimeOffset(2026, 9, 16, 8, 0, 0, TimeSpan.Zero),
            new GenerationProfile(
                customProfileId,
                "Code",
                "Initial instructions",
                new InferenceGenerationOptions(temperature: 0.2)));
        GenerationProfileRevision latestRevision = new(
            latestRevisionId,
            firstRevisionId,
            new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero),
            new GenerationProfile(
                customProfileId,
                "Code",
                "Use the latest confirmed profile.",
                new InferenceGenerationOptions(
                    maxOutputTokens: 640,
                    temperature: 0.35,
                    reasoningEnabled: true,
                    reasoningBudgetTokens: 128)));
        GenerationProfileCatalog catalog = new(
            scope,
            GenerationProfile.CreateDefault(defaultProfileId),
            new List<GenerationProfileRevision> { firstRevision, latestRevision });
        ConversationGenerationSelection selection = new(
            conversationId,
            scope,
            customProfileId);
        InMemorySelectionStore selectionStore = new(selection);
        InMemoryCatalogStore catalogStore = new(catalog);
        InMemoryProviderConfigurationStore providerStore = new(
            new InferenceProviderConfiguration(
                scope.ProviderId,
                scope.ModelReference,
                contextSize: 8192,
                generation: new InferenceGenerationOptions(temperature: 0.99)));
        DateTimeOffset resolvedAt =
            new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(2));
        ConversationGenerationResolver resolver = new(
            selectionStore,
            catalogStore,
            providerStore,
            new FixedTimeProvider(resolvedAt));

        ResolvedConversationGeneration? resolved = await resolver.ResolveAsync(
            conversationId,
            messageId,
            triggerRevisionId,
            inputRevisionIds,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        ResolvedConversationGeneration generation = Assert.IsType<ResolvedConversationGeneration>(resolved);
        Assert.Equal("Use the latest confirmed profile.", generation.SystemInstructions);
        Assert.False(generation.Snapshot.Id.IsEmpty);
        Assert.Equal(conversationId, generation.Snapshot.ConversationId);
        Assert.Equal(messageId, generation.Snapshot.TriggeringUserMessageId);
        Assert.Equal(triggerRevisionId, generation.Snapshot.TriggeringUserMessageRevisionId);
        Assert.Equal(inputRevisionIds, generation.Snapshot.InputMessageRevisionIds);
        Assert.Equal(resolvedAt.ToUniversalTime(), generation.Snapshot.CapturedAtUtc);
        Assert.Equal(scope.ProviderId, generation.Snapshot.ProviderId);
        Assert.Equal(scope.ModelReference, generation.Snapshot.ModelReference);
        Assert.Equal(8192, generation.Snapshot.ContextSize);
        Assert.Equal(customProfileId, generation.Snapshot.ProfileId);
        Assert.Equal(latestRevisionId, generation.Snapshot.ProfileRevisionId);
        Assert.Equal(640, generation.Snapshot.GenerationOptions.MaxOutputTokens);
        Assert.Equal(0.35, generation.Snapshot.GenerationOptions.Temperature);
        Assert.True(generation.Snapshot.GenerationOptions.ReasoningEnabled);
        Assert.Equal(128, generation.Snapshot.GenerationOptions.ReasoningBudgetTokens);
        Assert.NotEqual(0.99, generation.Snapshot.GenerationOptions.Temperature);
    }

    [Fact]
    public async Task ResolveAsyncKeepsDefaultProfileRevisionlessAndUsesProviderDefaults()
    {
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId triggerRevisionId = MessageRevisionId.New();
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfileId defaultProfileId = GenerationProfileId.New();
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            defaultProfileId);
        ConversationGenerationResolver resolver = new(
            new InMemorySelectionStore(
                new ConversationGenerationSelection(
                    conversationId,
                    scope,
                    defaultProfileId)),
            new InMemoryCatalogStore(catalog),
            new InMemoryProviderConfigurationStore(
                new InferenceProviderConfiguration(
                    scope.ProviderId,
                    scope.ModelReference,
                    contextSize: 4096,
                    generation: new InferenceGenerationOptions(
                        maxOutputTokens: 222,
                        temperature: 0.8))),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        ResolvedConversationGeneration? resolved = await resolver.ResolveAsync(
            conversationId,
            messageId,
            triggerRevisionId,
            new[] { triggerRevisionId },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        ResolvedConversationGeneration generation = Assert.IsType<ResolvedConversationGeneration>(resolved);
        Assert.Null(generation.SystemInstructions);
        Assert.Equal(defaultProfileId, generation.Snapshot.ProfileId);
        Assert.Null(generation.Snapshot.ProfileRevisionId);
        Assert.True(generation.Snapshot.HasProfileSelection);
        Assert.Equal(4096, generation.Snapshot.ContextSize);
        Assert.True(generation.Snapshot.GenerationOptions.UsesOnlyProviderDefaults);
        Assert.Null(generation.Snapshot.GenerationOptions.MaxOutputTokens);
        Assert.Null(generation.Snapshot.GenerationOptions.Temperature);
    }

    [Fact]
    public async Task ResolveAsyncUsesBranchSelectionBeforeLegacyConversationFallback()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationBranchId legacyFallbackBranchId = ConversationBranchId.New();
        ConversationBranchId scopedBranchId = ConversationBranchId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId revisionId = MessageRevisionId.New();
        GenerationProfileModelScope legacyScope = new(
            "provider.legacy",
            "owner/model-legacy");
        GenerationProfileModelScope scopedScope = new(
            "provider.branch",
            "owner/model-branch");
        GenerationProfileId legacyProfileId = GenerationProfileId.New();
        GenerationProfileId scopedProfileId = GenerationProfileId.New();
        ConversationGenerationResolver resolver = new(
            new InMemorySelectionStore(
                new ConversationGenerationSelection(
                    conversationId,
                    legacyScope,
                    legacyProfileId),
                new ConversationGenerationSelection(
                    conversationId,
                    scopedBranchId,
                    scopedScope,
                    scopedProfileId)),
            new InMemoryCatalogStore(
                GenerationProfileCatalog.CreateEmpty(legacyScope, legacyProfileId),
                GenerationProfileCatalog.CreateEmpty(scopedScope, scopedProfileId)),
            new InMemoryProviderConfigurationStore(
                new InferenceProviderConfiguration(
                    legacyScope.ProviderId,
                    legacyScope.ModelReference),
                new InferenceProviderConfiguration(
                    scopedScope.ProviderId,
                    scopedScope.ModelReference)),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        ResolvedConversationGeneration scoped = Assert.IsType<ResolvedConversationGeneration>(
            await resolver.ResolveAsync(
                conversationId,
                scopedBranchId,
                messageId,
                revisionId,
                new[] { revisionId },
                contextRevision: null,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        ResolvedConversationGeneration fallback = Assert.IsType<ResolvedConversationGeneration>(
            await resolver.ResolveAsync(
                conversationId,
                legacyFallbackBranchId,
                messageId,
                revisionId,
                new[] { revisionId },
                contextRevision: null,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));

        Assert.Equal(scopedScope.ProviderId, scoped.Snapshot.ProviderId);
        Assert.Equal(scopedScope.ModelReference, scoped.Snapshot.ModelReference);
        Assert.Equal(scopedProfileId, scoped.Snapshot.ProfileId);
        Assert.Equal(legacyScope.ProviderId, fallback.Snapshot.ProviderId);
        Assert.Equal(legacyScope.ModelReference, fallback.Snapshot.ModelReference);
        Assert.Equal(legacyProfileId, fallback.Snapshot.ProfileId);
    }

    [Fact]
    public async Task ResolveAsyncCapturesLegacySelectedProviderWithoutProfileSelection()
    {
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId triggerRevisionId = MessageRevisionId.New();
        InferenceProviderConfiguration configuration = new(
            "provider.alpha",
            "owner/model",
            contextSize: 6144,
            generation: new InferenceGenerationOptions(temperature: 0.45));
        ConversationGenerationResolver resolver = new(
            new InMemorySelectionStore(),
            new InMemoryCatalogStore(),
            new InMemoryProviderConfigurationStore(
                configuration.ProviderId,
                configuration),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        ResolvedConversationGeneration? resolved = await resolver.ResolveAsync(
            conversationId,
            messageId,
            triggerRevisionId,
            new[] { triggerRevisionId },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        ResolvedConversationGeneration generation = Assert.IsType<ResolvedConversationGeneration>(resolved);
        Assert.Null(generation.SystemInstructions);
        Assert.False(generation.Snapshot.HasProfileSelection);
        Assert.Null(generation.Snapshot.ProfileId);
        Assert.Null(generation.Snapshot.ProfileRevisionId);
        Assert.Equal(configuration.ProviderId, generation.Snapshot.ProviderId);
        Assert.Equal(configuration.ModelReference, generation.Snapshot.ModelReference);
        Assert.Equal(configuration.ContextSize, generation.Snapshot.ContextSize);
        Assert.Equal(0.45, generation.Snapshot.GenerationOptions.Temperature);
    }

    [Fact]
    public async Task ResolveAsyncReturnsNullWhenNoConversationOrProviderSelectionExists()
    {
        ConversationGenerationResolver resolver = new(
            new InMemorySelectionStore(),
            new InMemoryCatalogStore(),
            new InMemoryProviderConfigurationStore(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        ResolvedConversationGeneration? resolved = await ResolveSingleRevisionAsync(
            resolver,
            ConversationId.New(),
            MessageId.New()).ConfigureAwait(true);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsyncRejectsSelectionWhoseSavedProviderModelChanged()
    {
        ConversationId conversationId = ConversationId.New();
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model-a");
        GenerationProfileId defaultProfileId = GenerationProfileId.New();
        ConversationGenerationResolver resolver = new(
            new InMemorySelectionStore(
                new ConversationGenerationSelection(
                    conversationId,
                    scope,
                    defaultProfileId)),
            new InMemoryCatalogStore(
                GenerationProfileCatalog.CreateEmpty(scope, defaultProfileId)),
            new InMemoryProviderConfigurationStore(
                new InferenceProviderConfiguration(
                    scope.ProviderId,
                    "owner/model-b")),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveSingleRevisionAsync(
                resolver,
                conversationId,
                MessageId.New())).ConfigureAwait(true);

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsyncRejectsMissingCatalogOrSelectedProfile()
    {
        ConversationId conversationId = ConversationId.New();
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfileId missingProfileId = GenerationProfileId.New();
        InMemorySelectionStore selectionStore = new(
            new ConversationGenerationSelection(
                conversationId,
                scope,
                missingProfileId));
        InMemoryProviderConfigurationStore providerStore = new(
            new InferenceProviderConfiguration(
                scope.ProviderId,
                scope.ModelReference));
        ConversationGenerationResolver missingCatalogResolver = new(
            selectionStore,
            new InMemoryCatalogStore(),
            providerStore,
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResolveSingleRevisionAsync(
                missingCatalogResolver,
                conversationId,
                MessageId.New())).ConfigureAwait(true);

        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            GenerationProfileId.New());
        ConversationGenerationResolver missingProfileResolver = new(
            selectionStore,
            new InMemoryCatalogStore(catalog),
            providerStore,
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResolveSingleRevisionAsync(
                missingProfileResolver,
                conversationId,
                MessageId.New())).ConfigureAwait(true);
    }

    [Fact]
    public void GenerationSnapshotRejectsRevisionWithoutProfileIdentity()
    {
        InferenceProviderConfiguration configuration = new(
            "provider.alpha",
            "owner/model");
        MessageRevisionId revisionId = MessageRevisionId.New();

        Assert.Throws<ArgumentException>(() => new GenerationSnapshot(
            GenerationSnapshotId.New(),
            ConversationId.New(),
            MessageId.New(),
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            configuration,
            profileId: null,
            profileRevisionId: GenerationProfileRevisionId.New()));
    }

    private static Task<ResolvedConversationGeneration?> ResolveSingleRevisionAsync(
        ConversationGenerationResolver resolver,
        ConversationId conversationId,
        MessageId messageId)
    {
        MessageRevisionId revisionId = MessageRevisionId.New();
        return resolver.ResolveAsync(
            conversationId,
            messageId,
            revisionId,
            new[] { revisionId },
            TestContext.Current.CancellationToken);
    }

    private sealed class InMemorySelectionStore : IConversationBranchGenerationSelectionStore
    {
        private readonly Dictionary<
            (ConversationId ConversationId, ConversationBranchId? BranchId),
            ConversationGenerationSelection> _selections = [];

        public InMemorySelectionStore(params ConversationGenerationSelection[] selections)
        {
            foreach (ConversationGenerationSelection selection in selections)
            {
                _selections[(selection.ConversationId, selection.BranchId)] = selection;
            }
        }

        public Task<ConversationGenerationSelection?> LoadAsync(
            ConversationId conversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _selections.TryGetValue((conversationId, null), out ConversationGenerationSelection? selection);
            return Task.FromResult(selection);
        }

        public Task<ConversationGenerationSelection?> LoadAsync(
            ConversationId conversationId,
            ConversationBranchId branchId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_selections.TryGetValue(
                (conversationId, branchId),
                out ConversationGenerationSelection? selection))
            {
                return Task.FromResult<ConversationGenerationSelection?>(selection);
            }

            _selections.TryGetValue((conversationId, null), out selection);
            return Task.FromResult(selection);
        }

        public Task SaveAsync(
            ConversationGenerationSelection selection,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _selections[(selection.ConversationId, selection.BranchId)] = selection;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(
            ConversationId conversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool removed = false;
            foreach ((ConversationId ConversationId, ConversationBranchId? BranchId) key in
                _selections.Keys.Where(key => key.ConversationId == conversationId).ToArray())
            {
                removed |= _selections.Remove(key);
            }

            return Task.FromResult(removed);
        }

        public Task<bool> DeleteAsync(
            ConversationId conversationId,
            ConversationBranchId branchId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_selections.Remove((conversationId, branchId)));
        }
    }

    private sealed class InMemoryCatalogStore : IGenerationProfileCatalogStore
    {
        private readonly Dictionary<GenerationProfileModelScope, GenerationProfileCatalog> _catalogs = new();

        public InMemoryCatalogStore(params GenerationProfileCatalog[] catalogs)
        {
            foreach (GenerationProfileCatalog catalog in catalogs)
            {
                _catalogs[catalog.Scope] = catalog;
            }
        }

        public Task<GenerationProfileCatalog?> LoadAsync(
            GenerationProfileModelScope scope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _catalogs.TryGetValue(scope, out GenerationProfileCatalog? catalog);
            return Task.FromResult(catalog);
        }

        public Task SaveAsync(
            GenerationProfileCatalog catalog,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _catalogs[catalog.Scope] = catalog;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryProviderConfigurationStore : IInferenceProviderConfigurationStore
    {
        private readonly Dictionary<string, InferenceProviderConfiguration> _configurations =
            new(StringComparer.Ordinal);
        private string? _selectedProviderId;

        public InMemoryProviderConfigurationStore(
            params InferenceProviderConfiguration[] configurations)
        {
            foreach (InferenceProviderConfiguration configuration in configurations)
            {
                _configurations[configuration.ProviderId] = configuration;
            }
        }

        public InMemoryProviderConfigurationStore(
            string selectedProviderId,
            params InferenceProviderConfiguration[] configurations)
            : this(configurations)
        {
            _selectedProviderId = selectedProviderId;
        }

        public Task<string?> LoadSelectedProviderIdAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_selectedProviderId);
        }

        public Task<InferenceProviderConfiguration?> LoadAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _configurations.TryGetValue(providerId, out InferenceProviderConfiguration? configuration);
            return Task.FromResult(configuration);
        }

        public Task SaveAsync(
            InferenceProviderConfiguration configuration,
            bool selectProvider,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _configurations[configuration.ProviderId] = configuration;
            if (selectProvider)
            {
                _selectedProviderId = configuration.ProviderId;
            }

            return Task.CompletedTask;
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
