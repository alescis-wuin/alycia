using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.ViewModels;

public sealed class GenerationDualSelectorViewModelTests
{
    [Fact]
    public async Task LoadRestoresBranchScopedModelAndProfileSelection()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationBranchId branchId = ConversationBranchId.New();
        InferenceProviderConfiguration configuration = CreateConfiguration("owner/model-one:Q4_K_M");
        GenerationProfileModelScope scope = new(
            configuration.ProviderId,
            configuration.ModelReference!);
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            GenerationProfileId.New());
        InMemoryGenerationProfileCatalogStore catalogStore = new();
        catalogStore.Seed(catalog);
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        selectionStore.Seed(new ConversationGenerationSelection(
            conversationId,
            branchId,
            scope,
            catalog.DefaultProfile.Id));
        InMemoryInferenceModelLibraryStore libraryStore = new(new InferenceModelLibrary(
        [
            new InferenceModelLibraryEntry(
                configuration,
                new DateTimeOffset(2026, 9, 19, 20, 0, 0, TimeSpan.Zero)),
        ]));
        GenerationDualSelectorViewModel viewModel = new(
            libraryStore,
            catalogStore,
            selectionStore);

        await viewModel.LoadAsync(
            conversationId,
            branchId,
            configuration,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        GenerationDualSelectorModelItemViewModel selectedModel = Assert.IsType<GenerationDualSelectorModelItemViewModel>(
            viewModel.SelectedModel);
        GenerationDualSelectorProfileItemViewModel selectedProfile = Assert.IsType<GenerationDualSelectorProfileItemViewModel>(
            viewModel.SelectedProfile);
        Assert.Equal(scope, selectedModel.Scope);
        Assert.Equal(catalog.DefaultProfile.Id, selectedProfile.Id);
        Assert.True(selectedModel.IsBoundToActiveBranch);
        Assert.True(selectedProfile.IsBoundToActiveBranch);
        Assert.False(viewModel.HasPendingChanges);
        Assert.Equal("model-one | Default", viewModel.TriggerLabel);
    }

    [Fact]
    public async Task LegacyFallbackRemainsVisibleAndCanBePinnedToCurrentBranch()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationBranchId branchId = ConversationBranchId.New();
        InferenceProviderConfiguration configuration = CreateConfiguration("owner/model-one:Q4_K_M");
        GenerationProfileModelScope scope = new(
            configuration.ProviderId,
            configuration.ModelReference!);
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            GenerationProfileId.New());
        InMemoryGenerationProfileCatalogStore catalogStore = new();
        catalogStore.Seed(catalog);
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        selectionStore.Seed(new ConversationGenerationSelection(
            conversationId,
            scope,
            catalog.DefaultProfile.Id));
        InMemoryInferenceModelLibraryStore libraryStore = new(new InferenceModelLibrary(
        [
            new InferenceModelLibraryEntry(configuration, DateTimeOffset.UtcNow),
        ]));
        GenerationDualSelectorViewModel viewModel = new(
            libraryStore,
            catalogStore,
            selectionStore);

        await viewModel.LoadAsync(
            conversationId,
            branchId,
            configuration,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(viewModel.HasPendingChanges);
        Assert.Contains("Conversation fallback", viewModel.StatusText, StringComparison.Ordinal);

        viewModel.Open();
        await viewModel.ApplyAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        ConversationGenerationSelection saved = Assert.IsType<ConversationGenerationSelection>(
            selectionStore.LastSavedSelection);
        Assert.Equal(branchId, saved.BranchId);
        Assert.Equal(scope, saved.ModelScope);
        Assert.Equal(catalog.DefaultProfile.Id, saved.ProfileId);
        Assert.False(viewModel.HasPendingChanges);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public async Task ApplyingLibraryModelWithMissingCatalogPersistsDefaultCatalogAndBranchSelection()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationBranchId branchId = ConversationBranchId.New();
        InferenceProviderConfiguration current = CreateConfiguration("owner/model-one:Q4_K_M");
        InferenceProviderConfiguration alternate = CreateConfiguration("owner/model-two:Q5_K_M");
        InMemoryInferenceModelLibraryStore libraryStore = new(new InferenceModelLibrary(
        [
            new InferenceModelLibraryEntry(current, DateTimeOffset.UtcNow.AddMinutes(-1)),
            new InferenceModelLibraryEntry(alternate, DateTimeOffset.UtcNow),
        ]));
        InMemoryGenerationProfileCatalogStore catalogStore = new();
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        GenerationDualSelectorViewModel viewModel = new(
            libraryStore,
            catalogStore,
            selectionStore);

        await viewModel.LoadAsync(
            conversationId,
            branchId,
            current,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        viewModel.Open();
        viewModel.SelectedModel = Assert.Single(
            viewModel.Models,
            item => string.Equals(
                item.ModelReference,
                alternate.ModelReference,
                StringComparison.Ordinal));

        GenerationDualSelectorProfileItemViewModel selectedProfile = Assert.IsType<GenerationDualSelectorProfileItemViewModel>(
            viewModel.SelectedProfile);
        Assert.True(selectedProfile.IsDefault);
        Assert.Contains("will be pinned", viewModel.PreviewStatusText, StringComparison.Ordinal);

        await viewModel.ApplyAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, catalogStore.SaveCount);
        GenerationProfileCatalog savedCatalog = Assert.IsType<GenerationProfileCatalog>(
            catalogStore.LastSavedCatalog);
        Assert.Equal(alternate.ProviderId, savedCatalog.Scope.ProviderId);
        Assert.Equal(alternate.ModelReference, savedCatalog.Scope.ModelReference);
        ConversationGenerationSelection savedSelection = Assert.IsType<ConversationGenerationSelection>(
            selectionStore.LastSavedSelection);
        Assert.Equal(savedCatalog.Scope, savedSelection.ModelScope);
        Assert.Equal(savedCatalog.DefaultProfile.Id, savedSelection.ProfileId);
    }

    [Fact]
    public async Task CurrentSavedModelOutsideLibraryIsProjectedAsCompatibilityEntry()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationBranchId branchId = ConversationBranchId.New();
        InferenceProviderConfiguration current = CreateConfiguration("owner/current-model:Q4_K_M");
        GenerationDualSelectorViewModel viewModel = new(
            new InMemoryInferenceModelLibraryStore(),
            new InMemoryGenerationProfileCatalogStore(),
            new InMemoryConversationGenerationSelectionStore());

        await viewModel.LoadAsync(
            conversationId,
            branchId,
            current,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        GenerationDualSelectorModelItemViewModel item = Assert.Single(viewModel.Models);
        Assert.False(item.IsSavedInLibrary);
        Assert.True(item.IsCurrentSavedModel);
        Assert.Equal("current-model | Provider defaults", viewModel.TriggerLabel);
        Assert.Equal(item, viewModel.SelectedModel);
    }

    [Fact]
    public async Task BoundModelOutsideLibraryRemainsVisibleInsteadOfBeingSilentlyReplaced()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationBranchId branchId = ConversationBranchId.New();
        InferenceProviderConfiguration current = CreateConfiguration("owner/current-model:Q4_K_M");
        InferenceProviderConfiguration bound = CreateConfiguration("owner/bound-model:Q5_K_M");
        GenerationProfileModelScope boundScope = new(bound.ProviderId, bound.ModelReference!);
        GenerationProfileCatalog boundCatalog = GenerationProfileCatalog.CreateEmpty(
            boundScope,
            GenerationProfileId.New());
        InMemoryGenerationProfileCatalogStore catalogStore = new();
        catalogStore.Seed(boundCatalog);
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        selectionStore.Seed(new ConversationGenerationSelection(
            conversationId,
            branchId,
            boundScope,
            boundCatalog.DefaultProfile.Id));
        InMemoryInferenceModelLibraryStore libraryStore = new(new InferenceModelLibrary(
        [
            new InferenceModelLibraryEntry(current, DateTimeOffset.UtcNow),
        ]));
        GenerationDualSelectorViewModel viewModel = new(
            libraryStore,
            catalogStore,
            selectionStore);

        await viewModel.LoadAsync(
            conversationId,
            branchId,
            current,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, viewModel.Models.Count);
        GenerationDualSelectorModelItemViewModel boundItem = Assert.Single(
            viewModel.Models,
            item => item.Scope == boundScope);
        Assert.False(boundItem.IsSavedInLibrary);
        Assert.True(boundItem.IsBoundToActiveBranch);
        Assert.Equal(boundItem, viewModel.SelectedModel);
        Assert.Equal("bound-model | Default", viewModel.TriggerLabel);
    }

    [Fact]
    public async Task ClearRemovesPreviousBranchProjection()
    {
        ConversationId conversationId = ConversationId.New();
        ConversationBranchId branchId = ConversationBranchId.New();
        InferenceProviderConfiguration configuration = CreateConfiguration("owner/model-one:Q4_K_M");
        GenerationProfileModelScope scope = new(
            configuration.ProviderId,
            configuration.ModelReference!);
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            GenerationProfileId.New());
        InMemoryGenerationProfileCatalogStore catalogStore = new();
        catalogStore.Seed(catalog);
        InMemoryConversationGenerationSelectionStore selectionStore = new();
        selectionStore.Seed(new ConversationGenerationSelection(
            conversationId,
            branchId,
            scope,
            catalog.DefaultProfile.Id));
        GenerationDualSelectorViewModel viewModel = new(
            new InMemoryInferenceModelLibraryStore(new InferenceModelLibrary(
            [
                new InferenceModelLibraryEntry(configuration, DateTimeOffset.UtcNow),
            ])),
            catalogStore,
            selectionStore);

        await viewModel.LoadAsync(
            conversationId,
            branchId,
            configuration,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        viewModel.Open();

        viewModel.Clear();

        Assert.Empty(viewModel.Models);
        Assert.Empty(viewModel.Profiles);
        Assert.Null(viewModel.SelectedModel);
        Assert.Null(viewModel.SelectedProfile);
        Assert.Null(viewModel.ResolvedSelection);
        Assert.False(viewModel.IsOpen);
        Assert.Equal("Model | Profile", viewModel.TriggerLabel);
        Assert.False(viewModel.HasPendingChanges);
    }

    private static InferenceProviderConfiguration CreateConfiguration(string modelReference)
    {
        return new InferenceProviderConfiguration(
            "llama.cpp.cuda",
            modelReference,
            contextSize: 8192,
            generation: new InferenceGenerationOptions(reasoningEnabled: false));
    }
}
