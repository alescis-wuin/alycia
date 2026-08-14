using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

public sealed class LlamaCppProviderRuntime :
    IInferenceProviderRuntime,
    IInferenceProviderUpdateRuntime,
    IStreamingConversationResponder,
    IAsyncDisposable
{
    public const string ProviderId = "llama.cpp.cuda";

    public const string ProviderName = "llama.cpp CUDA";

    public string ValidatedVersion => LlamaCppReleasePolicy.ValidatedReleaseTag;

    private static readonly string[] _modelStartupFailureMarkers =
    [
        "model",
        "gguf",
        "hugging face",
        "failed to load",
        "tensor",
        "context",
        "kv cache",
        "out of memory",
        "cuda error",
    ];

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly string _runtimeDirectory;
    private readonly string _logDirectory;
    private readonly string _modelCacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly LlamaCppInstaller _installer;
    private readonly LlamaCppChatClient _chatClient;
    private readonly LlamaCppProviderTimeouts _timeouts;
    private Process? _serverProcess;
    private string? _executablePath;
    private string? _version;
    private string? _modelReference;
    private Uri? _endpoint;
    private string? _serverApiKey;
    private InferenceGenerationOptions _generationOptions = new();
    private InferenceProviderConfiguration? _activeConfiguration;
    private bool _disposed;

    public LlamaCppProviderRuntime(
        string runtimeDirectory,
        TimeProvider? timeProvider = null)
        : this(
            runtimeDirectory,
            timeProvider ?? TimeProvider.System,
            LlamaCppProviderTimeouts.Default)
    {
    }

    internal LlamaCppProviderRuntime(
        string runtimeDirectory,
        TimeProvider timeProvider,
        LlamaCppProviderTimeouts timeouts,
        HttpMessageHandler? httpMessageHandler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeouts);

        _runtimeDirectory = Path.GetFullPath(runtimeDirectory);
        _logDirectory = Path.Combine(_runtimeDirectory, "logs");
        _modelCacheDirectory = Path.Combine(_runtimeDirectory, "models");
        _timeouts = timeouts;
        _httpClient = httpMessageHandler is null
            ? new HttpClient()
            : new HttpClient(httpMessageHandler, disposeHandler: true);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _installer = new LlamaCppInstaller(_httpClient, timeProvider, _timeouts);
        _chatClient = new LlamaCppChatClient(_httpClient, _timeouts);
    }

    public async Task<InferenceProviderSnapshot> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (IsServerAlive())
            {
                return CreateSnapshot(
                    InferenceProviderState.Running,
                    detail: "Managed llama-server is running and ready for local chat.");
            }

            if (TryConsumeUnexpectedServerExit())
            {
                return CreateSnapshot(
                    InferenceProviderState.Faulted,
                    detail: "The local AI server stopped unexpectedly. Review the provider and start the model again.",
                    failureKind: InferenceProviderFailureKind.Faulted);
            }

            ClearExitedServer();
            LlamaCppInstallation? managedInstallation = await ReadManagedInstallationAsync(cancellationToken)
                .ConfigureAwait(false);

            if (managedInstallation is not null
                && File.Exists(managedInstallation.ExecutablePath))
            {
                InferenceProviderSnapshot? managedSnapshot = await ProbeExecutableAsync(
                    managedInstallation.ExecutablePath,
                    managedInstallation.Version,
                    cancellationToken).ConfigureAwait(false);

                if (managedSnapshot is not null)
                {
                    return managedSnapshot;
                }

                _executablePath = null;
                _version = null;
                _endpoint = null;
            }

            string? externalExecutable = LlamaCppInstaller.ResolveCommand("llama-server");

            if (externalExecutable is not null)
            {
                InferenceProviderSnapshot? externalSnapshot = await ProbeExecutableAsync(
                    externalExecutable,
                    expectedVersion: null,
                    cancellationToken).ConfigureAwait(false);

                if (externalSnapshot is not null)
                {
                    return externalSnapshot;
                }

                return CreateSnapshot(
                    InferenceProviderState.Unsupported,
                    detail: "A llama-server executable was found, but it did not expose an active CUDA backend. Install the managed CUDA build from Alicia.");
            }

            _executablePath = null;
            _version = null;
            _endpoint = null;

            return CreateSnapshot(
                InferenceProviderState.Missing,
                detail: $"llama.cpp CUDA is not installed. Alicia can build the validated managed release {LlamaCppReleasePolicy.ValidatedReleaseTag} locally.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<InferenceProviderSnapshot> InstallAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (IsServerAlive())
            {
                throw new InvalidOperationException(
                    "Stop llama.cpp before installing or replacing the managed runtime.");
            }

            LlamaCppReleaseDescriptor release = await _installer
                .GetValidatedReleaseAsync(
                    LlamaCppReleasePolicy.ValidatedReleaseTag,
                    LlamaCppReleasePolicy.ValidatedCommitSha,
                    cancellationToken)
                .ConfigureAwait(false);
            LlamaCppInstallation installation = await _installer
                .InstallAsync(_runtimeDirectory, release, progress, cancellationToken)
                .ConfigureAwait(false);

            InferenceProviderSnapshot snapshot = await ActivateManagedInstallationAsync(
                installation,
                cancellationToken).ConfigureAwait(false);
            progress?.Report(new InferenceProviderProgress(
                "Installation complete",
                $"Managed llama.cpp {installation.Version} is active. Previous managed releases are retained until explicit cleanup.",
                1.0));
            return snapshot;
        }
        catch (InferenceProviderException)
        {
            throw;
        }
        catch (PlatformNotSupportedException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Unsupported,
                "This system is missing requirements for the managed CUDA provider. Review the provider prerequisites and try again.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not download the local AI runtime. Check the network connection and try again.",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw CreateInstallFailure(exception);
        }
        catch (IOException exception)
        {
            throw CreateInstallFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw CreateInstallFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            throw CreateInstallFailure(exception);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw CreateInstallFailure(exception);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<InferenceProviderUpdateInfo> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await CheckForUpdateCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InferenceProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not check llama.cpp updates. Check the network connection and try again.",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (ArgumentException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (IOException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<InferenceProviderUpdateResult> UpdateAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (IsServerAlive())
            {
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Faulted,
                    "Stop the local AI model before updating llama.cpp.");
            }

            LlamaCppInstallation? currentInstallation = await ReadManagedInstallationAsync(cancellationToken)
                .ConfigureAwait(false);
            LlamaCppReleaseDescriptor latestRelease = await _installer
                .GetLatestReleaseAsync(cancellationToken)
                .ConfigureAwait(false);
            InferenceProviderUpdateInfo currentInfo = CreateUpdateInfo(
                currentInstallation,
                latestRelease);

            if (!currentInfo.IsManagedInstallation)
            {
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Missing,
                    "Alicia can update only a managed llama.cpp installation. Install the validated managed runtime first.");
            }

            if (!currentInfo.IsUpdateAvailable)
            {
                InferenceProviderSnapshot snapshot = await DetectManagedRuntimeCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new InferenceProviderUpdateResult(snapshot, currentInfo);
            }

            LlamaCppReleaseDescriptor release = await _installer
                .GetValidatedReleaseAsync(
                    LlamaCppReleasePolicy.ValidatedReleaseTag,
                    LlamaCppReleasePolicy.ValidatedCommitSha,
                    cancellationToken)
                .ConfigureAwait(false);
            LlamaCppInstallation installation = await _installer
                .InstallAsync(_runtimeDirectory, release, progress, cancellationToken)
                .ConfigureAwait(false);
            InferenceProviderSnapshot updatedSnapshot = await ActivateManagedInstallationAsync(
                installation,
                cancellationToken).ConfigureAwait(false);
            InferenceProviderUpdateInfo updatedInfo = CreateUpdateInfo(
                installation,
                latestRelease);
            progress?.Report(new InferenceProviderProgress(
                "Update complete",
                $"Managed llama.cpp {installation.Version} is active. The previous managed release was retained.",
                1.0));

            return new InferenceProviderUpdateResult(updatedSnapshot, updatedInfo);
        }
        catch (InferenceProviderException)
        {
            throw;
        }
        catch (PlatformNotSupportedException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Unsupported,
                "This system is missing requirements for the managed CUDA provider update. Review the provider prerequisites and try again.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not download the validated llama.cpp update. Check the network connection and try again.",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (ArgumentException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (IOException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            throw CreateUpdateFailure(exception);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw CreateUpdateFailure(exception);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<InferenceProviderSnapshot> StartAsync(
        InferenceProviderConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.Equals(configuration.ProviderId, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Configuration provider '{configuration.ProviderId}' does not match '{ProviderId}'.",
                nameof(configuration));
        }

        if (!configuration.HasModelReference)
        {
            throw new ArgumentException(
                "A Hugging Face model reference is required before starting llama.cpp.",
                nameof(configuration));
        }

        string normalizedModelReference;

        try
        {
            normalizedModelReference = LlamaCppModelReference.Normalize(
                configuration.ModelReference!);
        }
        catch (ArgumentException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Model,
                "The saved model reference is invalid. Review the model settings and try again.",
                exception);
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            InferenceProviderConfiguration normalizedConfiguration = new(
                ProviderId,
                normalizedModelReference,
                configuration.ContextSize,
                configuration.Generation);

            if (IsServerAlive())
            {
                if (Equals(_activeConfiguration, normalizedConfiguration))
                {
                    return CreateSnapshot(
                        InferenceProviderState.Running,
                        detail: "llama-server is already running with the selected saved configuration.");
                }

                await StopServerCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }

            ClearExitedServer();

            if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
            {
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Missing,
                    "The local AI runtime is not ready. Detect or install the provider before loading a model.");
            }

            Directory.CreateDirectory(_logDirectory);
            string logFilePath = Path.Combine(
                _logDirectory,
                $"server-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
            int port = LlamaCppLoopbackPortAllocator.Allocate();
            string apiKey = LlamaCppServerSecurity.CreateEphemeralApiKey();
            string[] arguments = LlamaCppServerCommand.CreateArguments(
                normalizedModelReference,
                logFilePath,
                port,
                configuration.ContextSize);
            ProcessStartInfo startInfo = new(_executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Directory.CreateDirectory(_modelCacheDirectory);
            startInfo.Environment["LLAMA_CACHE"] = _modelCacheDirectory;
            LlamaCppServerSecurity.ApplyApiKey(startInfo, apiKey);

            try
            {
                _serverProcess = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Unable to start llama-server.");
            }
            catch (InvalidOperationException exception)
            {
                throw CreateStartFailure(exception);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw CreateStartFailure(exception);
            }

            _endpoint = new Uri(
                $"http://127.0.0.1:{port}/",
                UriKind.Absolute);
            _serverApiKey = apiKey;
            _modelReference = normalizedModelReference;

            try
            {
                await WaitUntilHealthyAsync(
                    _serverProcess,
                    _endpoint,
                    apiKey,
                    logFilePath,
                    cancellationToken).ConfigureAwait(false);
                _generationOptions = normalizedConfiguration.Generation;
                _activeConfiguration = normalizedConfiguration;

                return CreateSnapshot(
                    InferenceProviderState.Running,
                    detail: $"llama-server is ready at {_endpoint}. Model: {normalizedModelReference}");
            }
            catch
            {
                await StopServerCoreAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<InferenceProviderSnapshot> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await StopServerCoreAsync(cancellationToken).ConfigureAwait(false);

            return CreateSnapshot(
                string.IsNullOrWhiteSpace(_executablePath)
                    ? InferenceProviderState.Missing
                    : InferenceProviderState.Ready,
                detail: string.IsNullOrWhiteSpace(_executablePath)
                    ? "llama.cpp CUDA is not installed."
                    : "llama.cpp CUDA is installed and stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        Uri? endpoint = _endpoint;
        string? apiKey = _serverApiKey;

        if (TryConsumeUnexpectedServerExit())
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Faulted,
                "The local AI server stopped unexpectedly. Review the provider and start the model again.");
        }

        if (!IsServerAlive()
            || endpoint is null
            || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Faulted,
                "The local AI server is not running. Start the configured model before sending a message.");
        }

        return _chatClient.StreamAsync(
            endpoint,
            apiKey,
            request,
            _generationOptions,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await StopServerCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            _httpClient.Dispose();
        }
    }

    internal static bool OutputShowsCuda(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("CUDA", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains(':'))
            {
                return true;
            }
        }

        return false;
    }

    internal static string? ParseVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("version:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["version:".Length..].Trim();
            }
        }

        return null;
    }

    private async Task<InferenceProviderUpdateInfo> CheckForUpdateCoreAsync(
        CancellationToken cancellationToken)
    {
        LlamaCppInstallation? installation = await ReadManagedInstallationAsync(cancellationToken)
            .ConfigureAwait(false);
        LlamaCppReleaseDescriptor latestRelease = await _installer
            .GetLatestReleaseAsync(cancellationToken)
            .ConfigureAwait(false);

        return CreateUpdateInfo(installation, latestRelease);
    }

    private static InferenceProviderUpdateInfo CreateUpdateInfo(
        LlamaCppInstallation? installation,
        LlamaCppReleaseDescriptor latestRelease)
    {
        ArgumentNullException.ThrowIfNull(latestRelease);

        bool isManagedInstallation = installation is not null
            && File.Exists(installation.ExecutablePath);
        string? installedVersion = isManagedInstallation
            ? installation!.Version
            : null;
        bool isLatestVersionValidated = string.Equals(
            latestRelease.TagName,
            LlamaCppReleasePolicy.ValidatedReleaseTag,
            StringComparison.Ordinal)
            && string.Equals(
                latestRelease.TargetCommitish,
                LlamaCppReleasePolicy.ValidatedCommitSha,
                StringComparison.OrdinalIgnoreCase);
        bool isUpdateAvailable = false;
        string detail;

        if (!isManagedInstallation || installedVersion is null)
        {
            detail = isLatestVersionValidated
                ? $"No managed llama.cpp release is active. Alicia validates {LlamaCppReleasePolicy.ValidatedReleaseTag}; install the managed runtime to use controlled updates."
                : $"No managed llama.cpp release is active. Alicia validates {LlamaCppReleasePolicy.ValidatedReleaseTag}; upstream latest is {latestRelease.TagName} and is not automatically trusted.";
        }
        else
        {
            int installedVsValidated = LlamaCppReleasePolicy.CompareReleaseTags(
                installedVersion,
                LlamaCppReleasePolicy.ValidatedReleaseTag);
            isUpdateAvailable = installedVsValidated < 0;

            if (isUpdateAvailable)
            {
                detail = $"Validated update available: {installedVersion} → {LlamaCppReleasePolicy.ValidatedReleaseTag}. The previous managed release will be kept.";
            }
            else if (installedVsValidated > 0)
            {
                detail = $"Managed release {installedVersion} is newer than Alicia's validated {LlamaCppReleasePolicy.ValidatedReleaseTag}; Alicia will not downgrade it automatically.";
            }
            else if (!isLatestVersionValidated)
            {
                detail = $"Managed release {installedVersion} matches Alicia's validated version. Upstream latest {latestRelease.TagName} has not been validated by this Alicia build.";
            }
            else
            {
                detail = $"Managed llama.cpp {installedVersion} matches Alicia's validated release and upstream latest.";
            }
        }

        return new InferenceProviderUpdateInfo(
            installedVersion,
            LlamaCppReleasePolicy.ValidatedReleaseTag,
            latestRelease.TagName,
            isManagedInstallation,
            isUpdateAvailable,
            isLatestVersionValidated,
            detail);
    }

    private async Task<InferenceProviderSnapshot> DetectManagedRuntimeCoreAsync(
        CancellationToken cancellationToken)
    {
        LlamaCppInstallation? installation = await ReadManagedInstallationAsync(cancellationToken)
            .ConfigureAwait(false);

        if (installation is null || !File.Exists(installation.ExecutablePath))
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Missing,
                "The managed llama.cpp runtime is no longer available. Reinstall the validated provider runtime.");
        }

        InferenceProviderSnapshot? snapshot = await ProbeExecutableAsync(
            installation.ExecutablePath,
            installation.Version,
            cancellationToken).ConfigureAwait(false);

        return snapshot ?? throw new InferenceProviderException(
            InferenceProviderFailureKind.Unsupported,
            "The managed llama.cpp runtime could not use CUDA. Review the NVIDIA driver and CUDA Toolkit, then try again.");
    }

    private async Task<InferenceProviderSnapshot> ActivateManagedInstallationAsync(
        LlamaCppInstallation installation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);

        string? previousExecutablePath = _executablePath;
        string? previousVersion = _version;
        string? previousModelReference = _modelReference;
        Uri? previousEndpoint = _endpoint;
        string? previousApiKey = _serverApiKey;

        bool activated = false;

        try
        {
            InferenceProviderSnapshot? snapshot = await ProbeExecutableAsync(
                installation.ExecutablePath,
                installation.Version,
                cancellationToken).ConfigureAwait(false);

            if (snapshot is null)
            {
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Unsupported,
                    "The validated local AI runtime could not use CUDA. The previous managed release remains active.");
            }

            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                _runtimeDirectory,
                installation,
                cancellationToken).ConfigureAwait(false);

            InferenceProviderSnapshot activatedSnapshot = CreateSnapshot(
                InferenceProviderState.Ready,
                detail: $"Managed llama.cpp {installation.Version} is active. Previous managed releases are retained until explicit cleanup.");
            activated = true;
            return activatedSnapshot;
        }
        finally
        {
            if (!activated)
            {
                _executablePath = previousExecutablePath;
                _version = previousVersion;
                _modelReference = previousModelReference;
                _endpoint = previousEndpoint;
                _serverApiKey = previousApiKey;
            }
        }
    }

    private async Task<InferenceProviderSnapshot?> ProbeExecutableAsync(
        string executablePath,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        string? workingDirectory = Path.GetDirectoryName(executablePath);
        ProcessCommandResult versionProbe = await RunProbeCommandAsync(
            executablePath,
            ["--version"],
            workingDirectory,
            cancellationToken).ConfigureAwait(false);

        if (versionProbe.ExitCode != 0)
        {
            return null;
        }

        ProcessCommandResult deviceProbe = await RunProbeCommandAsync(
            executablePath,
            ["--list-devices"],
            workingDirectory,
            cancellationToken).ConfigureAwait(false);

        if (deviceProbe.ExitCode != 0 || !OutputShowsCuda(deviceProbe.CombinedOutput))
        {
            return null;
        }

        _executablePath = Path.GetFullPath(executablePath);
        _version = ParseVersion(versionProbe.CombinedOutput) ?? expectedVersion;
        _endpoint = null;
        _serverApiKey = null;

        return CreateSnapshot(
            InferenceProviderState.Ready,
            detail: "llama.cpp CUDA detected. Enter a Hugging Face GGUF repository and start the local server.");
    }

    private async Task<LlamaCppInstallation?> ReadManagedInstallationAsync(
        CancellationToken cancellationToken)
    {
        string metadataPath = Path.Combine(_runtimeDirectory, "installation.json");

        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(metadataPath, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<LlamaCppInstallation>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task WaitUntilHealthyAsync(
        Process process,
        Uri endpoint,
        string apiKey,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        Uri healthUri = new(endpoint, "health");
        using CancellationTokenSource readinessSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readinessSource.CancelAfter(_timeouts.Readiness);

        while (true)
        {
            if (readinessSource.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Model,
                    "The selected model did not become ready in time. Review the model and provider settings, then try again.",
                    new TimeoutException(
                        $"llama-server did not become ready within {_timeouts.Readiness}."));
            }

            if (process.HasExited)
            {
                string logTail = ReadLogTail(logFilePath);
                throw CreateStartupExitFailure(TryGetExitCode(process), logTail);
            }

            try
            {
                using CancellationTokenSource requestSource =
                    CancellationTokenSource.CreateLinkedTokenSource(readinessSource.Token);
                requestSource.CancelAfter(_timeouts.HealthRequest);

                using HttpResponseMessage response = await _httpClient
                    .GetAsync(healthUri, requestSource.Token)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    await LlamaCppServerSessionProbe.VerifyOwnershipAsync(
                        _httpClient,
                        endpoint,
                        apiKey,
                        requestSource.Token).ConfigureAwait(false);
                    return;
                }

                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    throw new InferenceProviderException(
                        InferenceProviderFailureKind.Faulted,
                        "The local AI server reported an unexpected health state. Review the provider and try again.",
                        new InvalidOperationException(
                            $"llama-server health check returned HTTP {(int)response.StatusCode} ({response.StatusCode})."));
                }
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested
                    && !readinessSource.IsCancellationRequested)
            {
                // A single health request timed out. Retry until the bounded readiness deadline.
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested
                    && readinessSource.IsCancellationRequested)
            {
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Model,
                    "The selected model did not become ready in time. Review the model and provider settings, then try again.",
                    new TimeoutException(
                        $"llama-server did not become ready within {_timeouts.Readiness}."));
            }
            catch (HttpRequestException)
            {
                // The process can accept connections slightly after it starts. Retry until readiness expires.
            }
            catch (InferenceProviderException exception)
                when (exception.Kind == InferenceProviderFailureKind.Network
                    && !cancellationToken.IsCancellationRequested
                    && !readinessSource.IsCancellationRequested)
            {
                // Ownership probes are idempotent GETs. A bounded probe retry may still fail
                // transiently while llama-server finishes starting, so keep polling until readiness expires.
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(300),
                    readinessSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Model,
                    "The selected model did not become ready in time. Review the model and provider settings, then try again.",
                    new TimeoutException(
                        $"llama-server did not become ready within {_timeouts.Readiness}."));
            }
        }
    }

    private async Task<ProcessCommandResult> RunProbeCommandAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeouts.CommandProbe);

        try
        {
            return await ProcessCommandRunner.RunAsync(
                executablePath,
                arguments,
                workingDirectory,
                environment: null,
                timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Faulted,
                "The local AI runtime check did not finish in time. Review the provider installation and try again.",
                new TimeoutException(
                    $"llama-server probe exceeded {_timeouts.CommandProbe}.",
                    exception));
        }
        catch (InvalidOperationException exception)
        {
            throw CreateProbeFailure(exception);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw CreateProbeFailure(exception);
        }
    }

    private static InferenceProviderException CreateUpdateFailure(Exception exception)
    {
        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "Alicia could not complete the managed llama.cpp update. The previous managed release remains selected.",
            exception);
    }

    private static InferenceProviderException CreateInstallFailure(Exception exception)
    {
        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "Alicia could not install the local AI runtime. Review the provider prerequisites and try again.",
            exception);
    }

    private static InferenceProviderException CreateStartFailure(Exception exception)
    {
        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "Alicia could not start the local AI server. Review the provider installation and try again.",
            exception);
    }

    private static InferenceProviderException CreateProbeFailure(Exception exception)
    {
        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "Alicia could not inspect the local AI runtime. Review the provider installation and try again.",
            exception);
    }

    private async Task StopServerCoreAsync(CancellationToken cancellationToken)
    {
        Process? process = _serverProcess;
        _serverProcess = null;
        _endpoint = null;
        _serverApiKey = null;
        _activeConfiguration = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private bool IsServerAlive()
    {
        return _serverProcess is { HasExited: false }
            && _endpoint is not null
            && !string.IsNullOrWhiteSpace(_serverApiKey);
    }

    private void ClearExitedServer()
    {
        if (_serverProcess is null || !_serverProcess.HasExited)
        {
            return;
        }

        _serverProcess.Dispose();
        _serverProcess = null;
        _endpoint = null;
        _serverApiKey = null;
        _activeConfiguration = null;
    }

    private InferenceProviderSnapshot CreateSnapshot(
        InferenceProviderState state,
        string? detail,
        InferenceProviderFailureKind? failureKind = null)
    {
        return new InferenceProviderSnapshot(
            ProviderName,
            state,
            _version,
            !string.IsNullOrWhiteSpace(_executablePath),
            _executablePath,
            _modelReference,
            _endpoint,
            detail,
            failureKind);
    }

    private bool TryConsumeUnexpectedServerExit()
    {
        if (_serverProcess is null || !_serverProcess.HasExited)
        {
            return false;
        }

        ClearExitedServer();
        return true;
    }

    internal static InferenceProviderException CreateStartupExitFailure(
        int? exitCode,
        string logTail)
    {
        string diagnosticText = "llama-server exited before readiness"
            + (exitCode is null ? "." : $" with exit code {exitCode}.")
            + logTail;
        InvalidOperationException diagnostic = new(diagnosticText);

        if (LooksLikeModelStartupFailure(logTail))
        {
            return new InferenceProviderException(
                InferenceProviderFailureKind.Model,
                "The selected model could not be loaded by the local AI runtime. Review the model reference and settings, then try again.",
                diagnostic);
        }

        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "The local AI server stopped unexpectedly while starting. Review the provider installation and try again.",
            diagnostic);
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool LooksLikeModelStartupFailure(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return false;
        }

        return _modelStartupFailureMarkers.Any(
            marker => diagnostic.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadLogTail(string logFilePath)
    {
        try
        {
            if (!File.Exists(logFilePath))
            {
                return string.Empty;
            }

            string[] lines = File.ReadLines(logFilePath).TakeLast(12).ToArray();
            return lines.Length == 0
                ? string.Empty
                : $" Last log lines: {string.Join(" | ", lines.Select(line => line.Trim()))}";
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

}
