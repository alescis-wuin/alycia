using System.Runtime.CompilerServices;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.State;

namespace Alicia.Presentation.Tests.ViewModels;

internal sealed class InMemoryConversationRepository : IConversationRepository
{
    private readonly Dictionary<ConversationId, Conversation> _conversations = [];

    public Exception? SaveException { get; set; }

    public Task<bool> DeleteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_conversations.Remove(conversationId));
    }

    public Task<Conversation?> FindAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _conversations.TryGetValue(conversationId, out Conversation? conversation);
        return Task.FromResult(conversation);
    }

    public Task<IReadOnlyList<ConversationSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ConversationSummary> summaries = _conversations.Values
            .Select(ConversationSummary.FromConversation)
            .OrderByDescending(summary => summary.UpdatedAt)
            .ThenByDescending(summary => summary.CreatedAt)
            .ThenBy(summary => summary.Id.Value)
            .ToArray();

        return Task.FromResult(summaries);
    }

    public Task SaveAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();

        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }

        _conversations[conversation.Id] = conversation;
        return Task.CompletedTask;
    }

    public void Seed(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _conversations[conversation.Id] = conversation;
    }
}

internal sealed class DeterministicConversationResponder :
    IConversationResponder,
    IStreamingConversationResponder
{
    private readonly ConversationResponse _response;

    public DeterministicConversationResponder(string responseContent)
    {
        _response = new ConversationResponse(responseContent);
    }

    public int CallCount { get; private set; }

    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        return Task.FromResult(_response);
    }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        await Task.Yield();
        yield return new ConversationResponseChunk(_response.Content);
    }
}

internal sealed class CancellableConversationResponder :
    IConversationResponder,
    IStreamingConversationResponder
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public async Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _started.TrySetResult();

        await Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);

        return new ConversationResponse("Unreachable response");
    }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _started.TrySetResult();

        await Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);

        yield break;
    }
}

internal sealed class PausingStreamingConversationResponder : IStreamingConversationResponder
{
    private readonly TaskCompletionSource _firstChunkObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConversationResponseChunk _firstChunk;
    private readonly ConversationResponseChunk _secondChunk;

    public PausingStreamingConversationResponder(
        string firstContentDelta,
        string secondContentDelta)
    {
        _firstChunk = new ConversationResponseChunk(firstContentDelta);
        _secondChunk = new ConversationResponseChunk(secondContentDelta);
    }

    public Task FirstChunkObserved => _firstChunkObserved.Task;

    public int CallCount { get; private set; }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        yield return _firstChunk;
        _firstChunkObserved.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        yield return _secondChunk;
    }

    public void Release()
    {
        _release.TrySetResult();
    }
}

internal sealed class ReasoningConversationResponder : IStreamingConversationResponder
{
    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Yield();
        yield return new ConversationResponseChunk(
            ConversationResponseChunkKind.Reasoning,
            "Inspect premise\n\nVerify result");
        yield return new ConversationResponseChunk(
            ConversationResponseChunkKind.Content,
            "Final answer");
    }
}

internal sealed class FailOnceConversationResponder :
    IConversationResponder,
    IStreamingConversationResponder
{
    private readonly ConversationResponse _response;

    public FailOnceConversationResponder(string responseContent)
    {
        _response = new ConversationResponse(responseContent);
    }

    public int CallCount { get; private set; }

    public Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;

        return CallCount == 1
            ? Task.FromException<ConversationResponse>(
                new InvalidOperationException("Development responder failed."))
            : Task.FromResult(_response);
    }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;

        if (CallCount == 1)
        {
            yield return new ConversationResponseChunk("Discarded partial response");
            await Task.Yield();
            throw new InvalidOperationException("Development responder failed.");
        }

        await Task.Yield();
        yield return new ConversationResponseChunk(_response.Content);
    }
}

internal sealed class ProviderFailureConversationResponder : IStreamingConversationResponder
{
    private readonly InferenceProviderException _failure;

    public ProviderFailureConversationResponder(InferenceProviderException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _failure = failure;
    }

    public int CallCount { get; private set; }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        yield return new ConversationResponseChunk("Discarded partial response");
        await Task.Yield();
        throw _failure;
    }
}

internal sealed class StubInferenceProviderRuntime :
    IInferenceProviderRuntime,
    IInferenceProviderUpdateRuntime,
    IInferenceProviderMaintenanceRuntime,
    IInferenceProviderObservabilityRuntime
{
    private const string ProviderName = "llama.cpp CUDA";

    public StubInferenceProviderRuntime(InferenceProviderState initialState = InferenceProviderState.Running)
    {
        Current = CreateSnapshot(initialState, modelReference: initialState == InferenceProviderState.Running
            ? "owner/model-GGUF:Q4_K_M"
            : null);
        UpdateInfo = initialState == InferenceProviderState.Missing
            ? new InferenceProviderUpdateInfo(
                installedVersion: null,
                validatedVersion: ValidatedVersion,
                latestVersion: ValidatedVersion,
                isManagedInstallation: false,
                isUpdateAvailable: false,
                isLatestVersionValidated: true,
                detail: "No managed runtime installed.")
            : CreateUpToDateInfo();
        StorageInfo = initialState == InferenceProviderState.Missing
            ? new InferenceProviderStorageInfo(
                hasManagedRuntime: false,
                managedVersion: null,
                retainedReleaseCount: 0,
                runtimeBytes: 0,
                modelCacheBytes: 2048)
            : new InferenceProviderStorageInfo(
                hasManagedRuntime: true,
                managedVersion: ValidatedVersion,
                retainedReleaseCount: 1,
                runtimeBytes: 4096,
                modelCacheBytes: 2048);
    }

    public InferenceProviderSnapshot Current { get; private set; }

    public string ValidatedVersion => "b10435";

    public InferenceProviderUpdateInfo UpdateInfo { get; set; }

    public InferenceProviderStorageInfo StorageInfo { get; set; }

    public InferenceProviderGenerationObservation? LatestGenerationObservation { get; set; }

    public int InspectStorageCount { get; private set; }

    public int CleanupRetainedReleasesCount { get; private set; }

    public int UninstallCount { get; private set; }

    public InferenceProviderRemovalMode? LastRemovalMode { get; private set; }

    public int CheckUpdateCount { get; private set; }

    public int UpdateCount { get; private set; }

    public int DetectCount { get; private set; }

    public int InstallCount { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public string? LastStartedModel => LastStartedConfiguration?.ModelReference;

    public InferenceProviderConfiguration? LastStartedConfiguration { get; private set; }

    public Exception? StartException { get; set; }

    public Exception? DetectException { get; set; }

    public Exception? InstallException { get; set; }

    public Exception? CheckUpdateException { get; set; }

    public Exception? UpdateException { get; set; }

    public Exception? InspectStorageException { get; set; }

    public Exception? MaintenanceException { get; set; }

    public Task<InferenceProviderSnapshot> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DetectCount++;

        if (DetectException is not null)
        {
            throw DetectException;
        }

        return Task.FromResult(Current);
    }

    public async Task<InferenceProviderSnapshot> InstallAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InstallCount++;

        if (InstallException is not null)
        {
            throw InstallException;
        }

        progress?.Report(new InferenceProviderProgress(
            "Checking prerequisites",
            "Test prerequisites ready.",
            0.10));
        progress?.Report(new InferenceProviderProgress(
            "Installation complete",
            "Test provider installed.",
            1.0));
        await Task.Yield();
        Current = CreateSnapshot(InferenceProviderState.Ready);
        UpdateInfo = CreateUpToDateInfo();
        StorageInfo = CreateManagedStorageInfo();
        return Current;
    }

    public Task<InferenceProviderUpdateInfo> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckUpdateCount++;

        if (CheckUpdateException is not null)
        {
            throw CheckUpdateException;
        }

        return Task.FromResult(UpdateInfo);
    }

    public async Task<InferenceProviderUpdateResult> UpdateAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateCount++;

        if (UpdateException is not null)
        {
            throw UpdateException;
        }

        progress?.Report(new InferenceProviderProgress(
            "Updating runtime",
            "Installing Alicia validated release.",
            0.5));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Current = CreateSnapshot(InferenceProviderState.Ready);
        UpdateInfo = CreateUpToDateInfo();
        StorageInfo = CreateManagedStorageInfo();
        progress?.Report(new InferenceProviderProgress(
            "Update complete",
            "Validated runtime active.",
            1.0));

        return new InferenceProviderUpdateResult(Current, UpdateInfo);
    }

    public Task<InferenceProviderStorageInfo> InspectStorageAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InspectStorageCount++;

        if (InspectStorageException is not null)
        {
            throw InspectStorageException;
        }

        return Task.FromResult(StorageInfo);
    }

    public async Task<InferenceProviderMaintenanceResult> CleanupRetainedReleasesAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CleanupRetainedReleasesCount++;

        if (MaintenanceException is not null)
        {
            throw MaintenanceException;
        }

        progress?.Report(new InferenceProviderProgress(
            "Cleaning retained releases",
            "Removing inactive managed releases."));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        StorageInfo = new InferenceProviderStorageInfo(
            StorageInfo.HasManagedRuntime,
            StorageInfo.ManagedVersion,
            retainedReleaseCount: 0,
            runtimeBytes: Math.Max(0, StorageInfo.RuntimeBytes - 1024),
            modelCacheBytes: StorageInfo.ModelCacheBytes);
        Current = CreateSnapshot(InferenceProviderState.Ready, Current.ModelReference);
        const string Detail = "Retained runtime releases removed; active runtime and model cache preserved.";
        progress?.Report(new InferenceProviderProgress(
            "Cleanup complete",
            Detail,
            1.0));

        return new InferenceProviderMaintenanceResult(
            Current,
            StorageInfo,
            reclaimedBytes: 1024,
            detail: Detail);
    }

    public async Task<InferenceProviderMaintenanceResult> UninstallAsync(
        InferenceProviderRemovalMode mode,
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        cancellationToken.ThrowIfCancellationRequested();
        UninstallCount++;
        LastRemovalMode = mode;

        if (MaintenanceException is not null)
        {
            throw MaintenanceException;
        }

        progress?.Report(new InferenceProviderProgress(
            "Removing managed runtime",
            "Removing explicitly confirmed provider storage."));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        bool removeModelCache = mode == InferenceProviderRemovalMode.RuntimeAndModelCache;
        long preservedModelBytes = removeModelCache ? 0 : StorageInfo.ModelCacheBytes;
        long reclaimedBytes = StorageInfo.RuntimeBytes
            + (removeModelCache ? StorageInfo.ModelCacheBytes : 0);
        StorageInfo = new InferenceProviderStorageInfo(
            hasManagedRuntime: false,
            managedVersion: null,
            retainedReleaseCount: 0,
            runtimeBytes: 0,
            modelCacheBytes: preservedModelBytes);
        Current = CreateSnapshot(InferenceProviderState.Missing);
        string detail = removeModelCache
            ? "Managed runtime and local model cache removed; provider configuration and conversations preserved."
            : "Managed runtime removed; local model cache, provider configuration, and conversations preserved.";
        progress?.Report(new InferenceProviderProgress(
            "Uninstall complete",
            detail,
            1.0));

        return new InferenceProviderMaintenanceResult(
            Current,
            StorageInfo,
            reclaimedBytes,
            detail);
    }

    public Task<InferenceProviderSnapshot> StartAsync(
        InferenceProviderConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        LastStartedConfiguration = configuration;

        if (StartException is not null)
        {
            throw StartException;
        }

        Current = CreateSnapshot(InferenceProviderState.Running, configuration.ModelReference);
        return Task.FromResult(Current);
    }

    public Task<InferenceProviderSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCount++;
        Current = CreateSnapshot(InferenceProviderState.Ready, Current.ModelReference);
        return Task.FromResult(Current);
    }

    private InferenceProviderStorageInfo CreateManagedStorageInfo()
    {
        return new InferenceProviderStorageInfo(
            hasManagedRuntime: true,
            managedVersion: ValidatedVersion,
            retainedReleaseCount: 0,
            runtimeBytes: 4096,
            modelCacheBytes: StorageInfo.ModelCacheBytes);
    }

    private InferenceProviderUpdateInfo CreateUpToDateInfo()
    {
        return new InferenceProviderUpdateInfo(
            installedVersion: ValidatedVersion,
            validatedVersion: ValidatedVersion,
            latestVersion: ValidatedVersion,
            isManagedInstallation: true,
            isUpdateAvailable: false,
            isLatestVersionValidated: true,
            detail: "Managed runtime is up to date.");
    }

    private static InferenceProviderSnapshot CreateSnapshot(
        InferenceProviderState state,
        string? modelReference = null)
    {
        return new InferenceProviderSnapshot(
            ProviderName,
            state,
            version: "test",
            isCudaEnabled: state != InferenceProviderState.Missing,
            executablePath: state == InferenceProviderState.Missing ? null : "/test/llama-server",
            modelReference: modelReference,
            endpoint: state == InferenceProviderState.Running
                ? new Uri("http://127.0.0.1:8080/")
                : null,
            detail: state.ToString());
    }
}

internal sealed class EmptyInferenceProviderRegistry : IInferenceProviderRegistry
{
    public IReadOnlyList<InferenceProviderDescriptor> Providers { get; } = [];

    public string? SelectedProviderId => null;

    public void SelectProvider(string providerId)
    {
        throw new KeyNotFoundException($"Unknown test provider '{providerId}'.");
    }

    public IInferenceProviderRuntime GetRequiredRuntime(string providerId)
    {
        throw new KeyNotFoundException($"Unknown test provider '{providerId}'.");
    }
}

internal sealed class StubInferenceProviderRegistry : IInferenceProviderRegistry
{
    private const string ProviderId = "llama.cpp.cuda";
    private readonly IInferenceProviderRuntime _runtime;

    public StubInferenceProviderRegistry(IInferenceProviderRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        Providers = [new InferenceProviderDescriptor(ProviderId, "llama.cpp CUDA")];
    }

    public IReadOnlyList<InferenceProviderDescriptor> Providers { get; }

    public string? SelectedProviderId { get; private set; }

    public int SelectCount { get; private set; }

    public void SelectProvider(string providerId)
    {
        if (!string.Equals(providerId, ProviderId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException($"Unknown test provider '{providerId}'.");
        }

        SelectedProviderId = providerId;
        SelectCount++;
    }

    public IInferenceProviderRuntime GetRequiredRuntime(string providerId)
    {
        if (!string.Equals(providerId, ProviderId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException($"Unknown test provider '{providerId}'.");
        }

        return _runtime;
    }
}

internal sealed class StubInferenceProviderConfigurationStore : IInferenceProviderConfigurationStore
{
    private readonly Dictionary<string, InferenceProviderConfiguration> _configurations =
        new(StringComparer.Ordinal);

    public StubInferenceProviderConfigurationStore(
        InferenceProviderConfiguration? initialConfiguration = null,
        bool selectInitialProvider = true)
    {
        if (initialConfiguration is not null)
        {
            _configurations[initialConfiguration.ProviderId] = initialConfiguration;

            if (selectInitialProvider)
            {
                SelectedProviderId = initialConfiguration.ProviderId;
            }
        }
    }

    public string? SelectedProviderId { get; private set; }

    public int SaveCount { get; private set; }

    public InferenceProviderConfiguration? LastSavedConfiguration { get; private set; }

    public Task<string?> LoadSelectedProviderIdAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SelectedProviderId);
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
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        LastSavedConfiguration = configuration;
        _configurations[configuration.ProviderId] = configuration;

        if (selectProvider)
        {
            SelectedProviderId = configuration.ProviderId;
        }

        return Task.CompletedTask;
    }
}

internal sealed class StubConversationUiStateStore : IConversationUiStateStore
{
    public StubConversationUiStateStore(ConversationUiStateSnapshot? snapshot = null)
    {
        Snapshot = snapshot ?? ConversationUiStateSnapshot.Default;
    }

    public ConversationUiStateSnapshot Snapshot { get; private set; }

    public int LoadCount { get; private set; }

    public int SaveCount { get; private set; }

    public Task<ConversationUiStateSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadCount++;
        return Task.FromResult(Snapshot);
    }

    public Task SaveAsync(
        ConversationUiStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        Snapshot = snapshot;
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class MutableTimeProvider : TimeProvider
{
    public MutableTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }
}
