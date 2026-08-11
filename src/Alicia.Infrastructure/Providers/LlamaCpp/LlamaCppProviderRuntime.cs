using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

public sealed class LlamaCppProviderRuntime :
    IInferenceProviderRuntime,
    IStreamingConversationResponder,
    IAsyncDisposable
{
    public const string ProviderName = "llama.cpp CUDA";

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly string _runtimeDirectory;
    private readonly string _settingsPath;
    private readonly string _logDirectory;
    private readonly string _modelCacheDirectory;
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly LlamaCppInstaller _installer;
    private readonly LlamaCppChatClient _chatClient;
    private Process? _serverProcess;
    private string? _executablePath;
    private string? _version;
    private string? _modelReference;
    private Uri? _endpoint;
    private bool _disposed;

    public LlamaCppProviderRuntime(
        string runtimeDirectory,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

        _runtimeDirectory = Path.GetFullPath(runtimeDirectory);
        _settingsPath = Path.Combine(_runtimeDirectory, "settings.json");
        _logDirectory = Path.Combine(_runtimeDirectory, "logs");
        _modelCacheDirectory = Path.Combine(_runtimeDirectory, "models");
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _installer = new LlamaCppInstaller(
            _httpClient,
            timeProvider ?? TimeProvider.System);
        _chatClient = new LlamaCppChatClient(_httpClient);
        _modelReference = ReadPersistedModelReference();
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
                detail: "llama.cpp CUDA is not installed. Alicia can download the latest official source release and compile llama-server locally.");
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

            LlamaCppInstallation installation = await _installer
                .InstallAsync(_runtimeDirectory, progress, cancellationToken)
                .ConfigureAwait(false);
            InferenceProviderSnapshot? snapshot = await ProbeExecutableAsync(
                installation.ExecutablePath,
                installation.Version,
                cancellationToken).ConfigureAwait(false);

            return snapshot ?? throw new InvalidOperationException(
                "The managed llama.cpp installation could not be validated as a CUDA runtime.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<InferenceProviderSnapshot> StartAsync(
        string modelReference,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalizedModelReference = LlamaCppModelReference.Normalize(modelReference);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (IsServerAlive())
            {
                if (string.Equals(
                    _modelReference,
                    normalizedModelReference,
                    StringComparison.Ordinal))
                {
                    return CreateSnapshot(
                        InferenceProviderState.Running,
                        detail: "llama-server is already running with the selected Hugging Face model.");
                }

                await StopServerCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }

            ClearExitedServer();

            if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
            {
                throw new InvalidOperationException(
                    "llama.cpp CUDA is not ready. Detect or install the provider before starting a model.");
            }

            Directory.CreateDirectory(_logDirectory);
            string logFilePath = Path.Combine(
                _logDirectory,
                $"server-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
            IReadOnlyList<string> arguments = LlamaCppServerCommand.CreateArguments(
                normalizedModelReference,
                logFilePath);
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

            _serverProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start llama-server.");
            _endpoint = new Uri(
                $"http://127.0.0.1:{LlamaCppServerCommand.DefaultPort}/",
                UriKind.Absolute);
            _modelReference = normalizedModelReference;

            try
            {
                await WaitUntilHealthyAsync(
                    _serverProcess,
                    _endpoint,
                    logFilePath,
                    cancellationToken).ConfigureAwait(false);
                await PersistModelReferenceAsync(
                    normalizedModelReference,
                    cancellationToken).ConfigureAwait(false);

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

        if (!IsServerAlive() || _endpoint is null)
        {
            throw new InvalidOperationException(
                "llama.cpp CUDA is not running. Start a Hugging Face model before sending a message.");
        }

        return _chatClient.StreamAsync(
            _endpoint,
            request,
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

    private async Task<InferenceProviderSnapshot?> ProbeExecutableAsync(
        string executablePath,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        string? workingDirectory = Path.GetDirectoryName(executablePath);
        ProcessCommandResult versionProbe = await ProcessCommandRunner.RunAsync(
                executablePath,
                ["--version"],
                workingDirectory,
                environment: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (versionProbe.ExitCode != 0)
        {
            return null;
        }

        ProcessCommandResult deviceProbe = await ProcessCommandRunner.RunAsync(
                executablePath,
                ["--list-devices"],
                workingDirectory,
                environment: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (deviceProbe.ExitCode != 0 || !OutputShowsCuda(deviceProbe.CombinedOutput))
        {
            return null;
        }

        _executablePath = Path.GetFullPath(executablePath);
        _version = ParseVersion(versionProbe.CombinedOutput) ?? expectedVersion;
        _endpoint = null;

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
        string logFilePath,
        CancellationToken cancellationToken)
    {
        Uri healthUri = new(endpoint, "health");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                string logTail = ReadLogTail(logFilePath);
                throw new InvalidOperationException(
                    $"llama-server exited before becoming ready.{logTail}");
            }

            try
            {
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(healthUri, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }

                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    throw new InvalidOperationException(
                        $"llama-server health check returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task StopServerCoreAsync(CancellationToken cancellationToken)
    {
        Process? process = _serverProcess;
        _serverProcess = null;
        _endpoint = null;

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
        return _serverProcess is { HasExited: false } && _endpoint is not null;
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
    }

    private InferenceProviderSnapshot CreateSnapshot(
        InferenceProviderState state,
        string? detail)
    {
        return new InferenceProviderSnapshot(
            ProviderName,
            state,
            _version,
            !string.IsNullOrWhiteSpace(_executablePath),
            _executablePath,
            _modelReference,
            _endpoint,
            detail);
    }

    private string? ReadPersistedModelReference()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            ProviderSettings? settings = JsonSerializer.Deserialize<ProviderSettings>(json);
            return string.IsNullOrWhiteSpace(settings?.ModelReference)
                ? null
                : settings.ModelReference.Trim();
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

    private async Task PersistModelReferenceAsync(
        string modelReference,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_runtimeDirectory);
        string temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(
            new ProviderSettings(modelReference),
            _jsonOptions);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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

    private sealed record ProviderSettings(string ModelReference);
}
