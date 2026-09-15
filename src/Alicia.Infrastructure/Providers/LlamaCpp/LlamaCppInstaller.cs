using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed record LlamaCppInstallation(
    string Version,
    string ExecutablePath,
    DateTimeOffset InstalledAtUtc);

internal sealed record LlamaCppReleaseDescriptor(
    string TagName,
    string TargetCommitish,
    Uri TarballUri);

internal sealed class LlamaCppInstaller
{
    private static readonly Uri _latestReleaseUri = new(
        "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest");
    private static readonly Uri _releaseByTagBaseUri = new(
        "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/");
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly LlamaCppProviderTimeouts _timeouts;
    private readonly LlamaCppIdempotentHttpRetryPolicy _retryPolicy;

    public LlamaCppInstaller(
        HttpClient httpClient,
        TimeProvider timeProvider,
        LlamaCppProviderTimeouts? timeouts = null,
        LlamaCppIdempotentHttpRetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _timeouts = timeouts ?? LlamaCppProviderTimeouts.Default;
        _retryPolicy = retryPolicy ?? LlamaCppIdempotentHttpRetryPolicy.Default;
    }

    public async Task<LlamaCppInstallation> InstallAsync(
        string runtimeDirectory,
        LlamaCppReleaseDescriptor release,
        IProgress<InferenceProviderProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentNullException.ThrowIfNull(release);
        ReportProgress(
            progress,
            "Checking prerequisites",
            "Checking Linux x64, CMake, C/C++ compilers, CUDA Toolkit, NVIDIA driver, and build backend.",
            0.02);
        EnsureSupportedPlatform();

        LlamaCppBuildToolchain toolchain = ResolveBuildToolchain();
        ReportProgress(
            progress,
            "Resolving release",
            $"Preparing Alicia-validated llama.cpp {release.TagName}.",
            0.10);

        string normalizedRuntimeDirectory = Path.GetFullPath(runtimeDirectory);
        string releasesDirectory = Path.Combine(normalizedRuntimeDirectory, "releases");
        string releaseDirectory = Path.Combine(releasesDirectory, release.TagName);
        string executablePath = Path.Combine(releaseDirectory, "llama-server");

        if (File.Exists(executablePath))
        {
            ReportProgress(
                progress,
                "Validating existing runtime",
                $"Checking the managed {release.TagName} llama-server for an active CUDA backend.",
                0.12);
            EnsureExecutableMode(executablePath);

            if (await IsCudaExecutableAsync(
                executablePath,
                releaseDirectory,
                cancellationToken).ConfigureAwait(false))
            {
                LlamaCppInstallation existingInstallation = new(
                    release.TagName,
                    executablePath,
                    _timeProvider.GetUtcNow());
                ReportProgress(
                    progress,
                    "Candidate ready",
                    $"Validated llama.cpp {release.TagName} with CUDA is ready for activation.",
                    0.99);
                return existingInstallation;
            }

            ReportProgress(
                progress,
                "Repairing runtime",
                "The managed llama-server is invalid or CPU-only. Rebuilding it with CUDA.",
                0.13);
            File.Delete(executablePath);
        }

        string stagingDirectory = Path.Combine(
            normalizedRuntimeDirectory,
            ".staging",
            Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(stagingDirectory, "llama.cpp.tar.gz");
        string extractDirectory = Path.Combine(stagingDirectory, "source");
        string buildDirectory = Path.Combine(stagingDirectory, "build");

        Directory.CreateDirectory(stagingDirectory);

        try
        {
            ReportProgress(
                progress,
                "Downloading source",
                $"Downloading official llama.cpp {release.TagName} source archive.",
                0.15);
            await DownloadAsync(
                release.TarballUri,
                archivePath,
                progress,
                cancellationToken).ConfigureAwait(false);

            ReportProgress(
                progress,
                "Extracting source",
                "Extracting the GitHub source archive into the isolated build workspace.",
                0.35);
            ExtractArchive(archivePath, extractDirectory);
            string sourceDirectory = ResolveSourceDirectory(extractDirectory);

            Dictionary<string, string?> environment = BuildEnvironment(toolchain);
            IReadOnlyList<string> configureArguments = CreateConfigureArguments(
                toolchain,
                sourceDirectory,
                buildDirectory);

            ReportProgress(
                progress,
                "Configuring CUDA build",
                $"Configuring llama.cpp with {toolchain.CmakeGenerator}, GGML_CUDA=ON, and llama-server enabled.",
                0.42);
            await RunRequiredAsync(
                toolchain.Cmake,
                configureArguments,
                sourceDirectory,
                environment,
                "llama.cpp CUDA configuration",
                cancellationToken).ConfigureAwait(false);

            int parallelism = Math.Max(1, Environment.ProcessorCount);
            IReadOnlyList<string> buildArguments =
            [
                "--build",
                buildDirectory,
                "--config",
                "Release",
                "--target",
                "llama-server",
                "--parallel",
                parallelism.ToString(CultureInfo.InvariantCulture),
            ];

            ReportProgress(
                progress,
                "Compiling llama-server",
                $"Compiling the CUDA server with up to {parallelism} parallel job(s).",
                0.50);
            await RunRequiredAsync(
                toolchain.Cmake,
                buildArguments,
                sourceDirectory,
                environment,
                "llama.cpp CUDA build",
                cancellationToken,
                outputLine: line =>
                {
                    if (!TryParseBuildFraction(line, out double buildFraction))
                    {
                        return;
                    }

                    double overallFraction = 0.50 + (buildFraction * 0.40);
                    ReportProgress(
                        progress,
                        "Compiling llama-server",
                        SanitizeProgressLine(line),
                        overallFraction);
                }).ConfigureAwait(false);

            string builtExecutable = Path.Combine(buildDirectory, "bin", "llama-server");

            if (!File.Exists(builtExecutable))
            {
                throw new InvalidOperationException(
                    "llama.cpp build completed without producing build/bin/llama-server.");
            }

            ReportProgress(
                progress,
                "Installing runtime",
                $"Copying llama-server into the managed {release.TagName} runtime.",
                0.92);
            Directory.CreateDirectory(releasesDirectory);
            Directory.CreateDirectory(releaseDirectory);
            File.Copy(builtExecutable, executablePath, overwrite: true);
            EnsureExecutableMode(executablePath);

            ReportProgress(
                progress,
                "Validating CUDA",
                "Running llama-server --list-devices to verify that the compiled runtime exposes CUDA.",
                0.96);
            if (!await IsCudaExecutableAsync(
                executablePath,
                releaseDirectory,
                cancellationToken).ConfigureAwait(false))
            {
                File.Delete(executablePath);
                throw new PlatformNotSupportedException(
                    "The compiled llama-server did not expose an active CUDA backend. Verify the NVIDIA driver and CUDA Toolkit, then retry installation.");
            }

            LlamaCppInstallation installation = new(
                release.TagName,
                executablePath,
                _timeProvider.GetUtcNow());
            ReportProgress(
                progress,
                "Finalizing installation",
                "The validated runtime is ready for atomic activation.",
                0.98);

            ReportProgress(
                progress,
                "Candidate ready",
                $"Validated llama.cpp {release.TagName} with CUDA is ready for activation.",
                0.99);
            return installation;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static async Task<bool> IsCudaExecutableAsync(
        string executablePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessCommandResult versionProbe = await ProcessCommandRunner.RunAsync(
                executablePath,
                ["--version"],
                workingDirectory,
                environment: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (versionProbe.ExitCode != 0)
        {
            return false;
        }

        ProcessCommandResult deviceProbe = await ProcessCommandRunner.RunAsync(
                executablePath,
                ["--list-devices"],
                workingDirectory,
                environment: null,
                cancellationToken)
            .ConfigureAwait(false);

        return deviceProbe.ExitCode == 0
            && LlamaCppProviderRuntime.OutputShowsCuda(deviceProbe.CombinedOutput);
    }

    internal static LlamaCppReleaseDescriptor ParseReleaseDescriptor(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        LatestReleaseResponse? response = JsonSerializer.Deserialize<LatestReleaseResponse>(json);

        if (response is null
            || string.IsNullOrWhiteSpace(response.TagName)
            || !IsSafeReleaseTag(response.TagName)
            || string.IsNullOrWhiteSpace(response.TargetCommitish)
            || !IsSafeCommitish(response.TargetCommitish)
            || string.IsNullOrWhiteSpace(response.TarballUrl)
            || !Uri.TryCreate(response.TarballUrl, UriKind.Absolute, out Uri? tarballUri)
            || tarballUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(tarballUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            || !tarballUri.AbsolutePath.StartsWith(
                "/repos/ggml-org/llama.cpp/tarball/",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "GitHub returned an invalid llama.cpp release descriptor.");
        }

        return new LlamaCppReleaseDescriptor(
            response.TagName.Trim(),
            response.TargetCommitish.Trim(),
            tarballUri);
    }

    private static bool IsSafeReleaseTag(string tagName)
    {
        string normalized = tagName.Trim();

        return normalized.Length is > 0 and <= 80
            && !normalized.Contains("..", StringComparison.Ordinal)
            && normalized.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_');
    }

    private static bool IsSafeCommitish(string commitish)
    {
        string normalized = commitish.Trim();

        return normalized.Length is > 0 and <= 80
            && !normalized.Contains("..", StringComparison.Ordinal)
            && normalized.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_' or '/');
    }

    internal static IReadOnlyList<string> CreateConfigureArguments(
        LlamaCppBuildToolchain toolchain,
        string sourceDirectory,
        string buildDirectory)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildDirectory);

        return
        [
            "-S",
            Path.GetFullPath(sourceDirectory),
            "-B",
            Path.GetFullPath(buildDirectory),
            "-G",
            toolchain.CmakeGenerator,
            "-DGGML_CUDA=ON",
            "-DGGML_NATIVE=ON",
            "-DBUILD_SHARED_LIBS=OFF",
            "-DLLAMA_BUILD_TESTS=OFF",
            "-DLLAMA_BUILD_EXAMPLES=OFF",
            "-DLLAMA_BUILD_TOOLS=ON",
            "-DLLAMA_BUILD_SERVER=ON",
            "-DLLAMA_BUILD_APP=OFF",
            "-DLLAMA_BUILD_UI=OFF",
            "-DLLAMA_OPENSSL=ON",
            "-DCMAKE_BUILD_TYPE=Release",
            $"-DCMAKE_C_COMPILER={toolchain.CCompiler}",
            $"-DCMAKE_CXX_COMPILER={toolchain.CxxCompiler}",
            $"-DCMAKE_CUDA_COMPILER={toolchain.CudaCompiler}",
        ];
    }

    internal static LlamaCppBuildToolchain ResolveBuildToolchain()
    {
        string? cmake = ResolveCommand("cmake");
        string? cCompiler = ResolveCommand("cc")
            ?? ResolveCommand("gcc")
            ?? ResolveCommand("clang");
        string? cxxCompiler = ResolveCommand("c++")
            ?? ResolveCommand("g++")
            ?? ResolveCommand("clang++");
        string? cudaCompiler = ResolveCudaCompiler();
        string? nvidiaSmi = ResolveCommand("nvidia-smi");
        string? ninja = ResolveCommand("ninja");
        string? make = ResolveCommand("make");
        List<string> missing = [];

        if (cmake is null)
        {
            missing.Add("cmake");
        }

        if (cCompiler is null)
        {
            missing.Add("C compiler (cc, gcc, or clang)");
        }

        if (cxxCompiler is null)
        {
            missing.Add("C++ compiler (c++, g++, or clang++)");
        }

        if (cudaCompiler is null)
        {
            missing.Add("CUDA compiler (nvcc)");
        }

        if (nvidiaSmi is null)
        {
            missing.Add("NVIDIA driver utility (nvidia-smi)");
        }

        if (ninja is null && make is null)
        {
            missing.Add("build backend (ninja or make)");
        }

        if (missing.Count > 0)
        {
            throw new PlatformNotSupportedException(
                $"Cannot build llama.cpp with CUDA. Missing system prerequisite(s): {string.Join(", ", missing)}.");
        }

        return new LlamaCppBuildToolchain(
            cmake!,
            cCompiler!,
            cxxCompiler!,
            cudaCompiler!,
            nvidiaSmi!,
            ninja is null ? "Unix Makefiles" : "Ninja");
    }

    internal Task<LlamaCppReleaseDescriptor> GetLatestReleaseAsync(
        CancellationToken cancellationToken)
    {
        return GetReleaseDescriptorAsync(
            CreateLatestReleaseRequest,
            "Alicia could not reach the llama.cpp release service after several attempts. Check the network connection and try again.",
            cancellationToken);
    }

    internal async Task<LlamaCppReleaseDescriptor> GetValidatedReleaseAsync(
        string expectedTag,
        string expectedCommitSha,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommitSha);

        LlamaCppReleaseDescriptor release = await GetReleaseDescriptorAsync(
            () => CreateReleaseByTagRequest(expectedTag),
            "Alicia could not verify the validated llama.cpp release after several attempts. Check the network connection and try again.",
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(release.TagName, expectedTag, StringComparison.Ordinal)
            || !string.Equals(release.TargetCommitish, expectedCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The validated llama.cpp release no longer resolves to Alicia's pinned source commit.");
        }

        return release with { TarballUri = LlamaCppReleasePolicy.ValidatedTarballUri };
    }

    private async Task<LlamaCppReleaseDescriptor> GetReleaseDescriptorAsync(
        Func<HttpRequestMessage> createRequest,
        string retryUserMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(retryUserMessage);

        using HttpResponseMessage response = await _retryPolicy.SendAsync(
            async token =>
            {
                using HttpRequestMessage request = createRequest();
                return await SendHttpAsync(request, token).ConfigureAwait(false);
            },
            retryUserMessage,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await LlamaCppTimeoutGuard.RunAsync(
            token => response.Content.ReadAsStringAsync(token),
            _timeouts.InstallerReadIdle,
            InferenceProviderFailureKind.Network,
            "The llama.cpp release service stopped responding. Check the network connection and try again.",
            cancellationToken)
            .ConfigureAwait(false);

        return ParseReleaseDescriptor(json);
    }

    private async Task DownloadAsync(
        Uri uri,
        string destinationPath,
        IProgress<InferenceProviderProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _retryPolicy.SendAsync(
            async token =>
            {
                using HttpRequestMessage request = CreateDownloadRequest(uri);
                return await SendHttpAsync(request, token).ConfigureAwait(false);
            },
            "Alicia could not start the llama.cpp source download after several attempts. Check the network connection and try again.",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        await using Stream input = await LlamaCppTimeoutGuard.RunAsync(
            token => response.Content.ReadAsStreamAsync(token),
            _timeouts.InstallerReadIdle,
            InferenceProviderFailureKind.Network,
            "The llama.cpp download did not begin in time. Check the network connection and try again.",
            cancellationToken)
            .ConfigureAwait(false);
        await using FileStream output = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        long downloadedBytes = 0;

        while (true)
        {
            int bytesRead = await LlamaCppTimeoutGuard.RunAsync(
                token => input.ReadAsync(buffer.AsMemory(), token).AsTask(),
                _timeouts.InstallerReadIdle,
                InferenceProviderFailureKind.Network,
                "The llama.cpp download stopped making progress. Check the network connection and try again.",
                cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
            {
                break;
            }

            await output
                .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);
            downloadedBytes += bytesRead;

            if (contentLength is > 0)
            {
                double downloadFraction = Math.Clamp(
                    downloadedBytes / (double)contentLength.Value,
                    0,
                    1);
                ReportProgress(
                    progress,
                    "Downloading source",
                    $"{FormatBytes(downloadedBytes)} / {FormatBytes(contentLength.Value)}",
                    0.15 + (downloadFraction * 0.17));
            }
            else
            {
                ReportProgress(
                    progress,
                    "Downloading source",
                    $"{FormatBytes(downloadedBytes)} downloaded.",
                    0.15);
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        ReportProgress(
            progress,
            "Downloading source",
            $"{FormatBytes(downloadedBytes)} downloaded.",
            0.32);
    }

    private static HttpRequestMessage CreateLatestReleaseRequest()
    {
        HttpRequestMessage request = new(HttpMethod.Get, _latestReleaseUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Alicia", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return request;
    }

    private static HttpRequestMessage CreateReleaseByTagRequest(string releaseTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseTag);

        Uri uri = new(_releaseByTagBaseUri, Uri.EscapeDataString(releaseTag.Trim()));
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Alicia", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return request;
    }

    private static HttpRequestMessage CreateDownloadRequest(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Alicia", "1.0"));
        return request;
    }

    private static void ExtractArchive(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        using FileStream archiveStream = File.OpenRead(archivePath);
        using GZipStream gzipStream = new(archiveStream, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(
            gzipStream,
            destinationDirectory,
            overwriteFiles: false);
    }

    private async Task<HttpResponseMessage> SendHttpAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LlamaCppTimeoutGuard.RunAsync(
                token => _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    token),
                _timeouts.InstallerResponseHeaders,
                InferenceProviderFailureKind.Network,
                "The llama.cpp download service did not respond in time. Check the network connection and try again.",
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not reach the llama.cpp download service. Check the network connection and try again.",
                exception);
        }
    }

    private static string ResolveSourceDirectory(string extractDirectory)
    {
        string[] directories = Directory.GetDirectories(extractDirectory);

        if (directories.Length != 1
            || !File.Exists(Path.Combine(directories[0], "CMakeLists.txt")))
        {
            throw new InvalidDataException(
                "The llama.cpp source archive does not contain the expected repository root.");
        }

        return directories[0];
    }

    private static async Task RunRequiredAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        string operation,
        CancellationToken cancellationToken,
        Action<string>? outputLine = null)
    {
        ProcessCommandResult result = await ProcessCommandRunner.RunAsync(
                executable,
                arguments,
                workingDirectory,
                environment,
                cancellationToken,
                outputLine)
            .ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            return;
        }

        string detail = string.IsNullOrWhiteSpace(result.CombinedOutput)
            ? "No diagnostic output was produced."
            : LastLines(result.CombinedOutput, 12);
        throw new InvalidOperationException(
            $"{operation} failed with exit code {result.ExitCode}. {detail}");
    }

    internal static bool TryParseBuildFraction(
        string line,
        out double fraction)
    {
        fraction = 0;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        int openBracket = line.IndexOf('[');
        int closeBracket = openBracket < 0
            ? -1
            : line.IndexOf(']', openBracket + 1);

        if (openBracket < 0 || closeBracket <= openBracket + 1)
        {
            return false;
        }

        ReadOnlySpan<char> token = line
            .AsSpan(openBracket + 1, closeBracket - openBracket - 1)
            .Trim();
        int slash = token.IndexOf('/');

        if (slash > 0
            && slash < token.Length - 1
            && int.TryParse(
                token[..slash],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int completed)
            && int.TryParse(
                token[(slash + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int total)
            && completed >= 0
            && total > 0)
        {
            fraction = Math.Clamp(completed / (double)total, 0, 1);
            return true;
        }

        if (token.Length > 1
            && token[^1] == '%'
            && double.TryParse(
                token[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double percent))
        {
            fraction = Math.Clamp(percent / 100, 0, 1);
            return true;
        }

        return false;
    }

    private static void ReportProgress(
        IProgress<InferenceProviderProgress>? progress,
        string stage,
        string detail,
        double fraction)
    {
        progress?.Report(new InferenceProviderProgress(
            stage,
            detail,
            Math.Clamp(fraction, 0, 1)));
    }

    private static string SanitizeProgressLine(string line)
    {
        string normalized = line.Trim();

        return normalized.Length <= 180
            ? normalized
            : $"{normalized[..177]}...";
    }

    private static string FormatBytes(long bytes)
    {
        const double BytesPerMiB = 1024d * 1024d;
        return $"{bytes / BytesPerMiB:0.0} MiB";
    }

    private static Dictionary<string, string?> BuildEnvironment(
        LlamaCppBuildToolchain toolchain)
    {
        string cudaRoot = Directory.GetParent(Path.GetDirectoryName(toolchain.CudaCompiler)!)!.FullName;
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string cudaBin = Path.GetDirectoryName(toolchain.CudaCompiler)!;

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CUDACXX"] = toolchain.CudaCompiler,
            ["CUDA_PATH"] = cudaRoot,
            ["CUDA_HOME"] = cudaRoot,
            ["PATH"] = string.Join(Path.PathSeparator, cudaBin, path),
        };
    }

    private static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsLinux()
            || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "Automatic native llama.cpp CUDA compilation currently supports Linux x64 only.");
        }
    }

    private static string? ResolveCudaCompiler()
    {
        foreach (string? candidate in new[]
        {
            Environment.GetEnvironmentVariable("CUDACXX"),
            CombineEnvironmentPath("CUDA_HOME", "bin", "nvcc"),
            CombineEnvironmentPath("CUDA_PATH", "bin", "nvcc"),
            "/opt/cuda/bin/nvcc",
            "/usr/local/cuda/bin/nvcc",
            ResolveCommand("nvcc"),
        })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? CombineEnvironmentPath(
        string environmentVariable,
        params string[] components)
    {
        string? root = Environment.GetEnvironmentVariable(environmentVariable);

        return string.IsNullOrWhiteSpace(root)
            ? null
            : Path.Combine(new[] { root }.Concat(components).ToArray());
    }

    internal static string? ResolveCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (Path.IsPathRooted(command) && File.Exists(command))
        {
            return Path.GetFullPath(command);
        }

        string executableName = OperatingSystem.IsWindows() && !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? $"{command}.exe"
            : command;
        string? pathValue = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, executableName);

            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    internal static async Task WriteInstallationMetadataAsync(
        string runtimeDirectory,
        LlamaCppInstallation installation,
        CancellationToken cancellationToken)
    {
        string metadataPath = Path.Combine(runtimeDirectory, "installation.json");
        string temporaryPath = $"{metadataPath}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(runtimeDirectory);
        string json = JsonSerializer.Serialize(
            installation,
            _jsonOptions);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureExecutableMode(string executablePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            executablePath,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
    }

    private static string LastLines(string value, int count)
    {
        return string.Join(
            " | ",
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(count)
                .Select(line => line.Trim()));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record LatestReleaseResponse(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("target_commitish")] string? TargetCommitish,
        [property: JsonPropertyName("tarball_url")] string? TarballUrl);
}

internal sealed record LlamaCppBuildToolchain(
    string Cmake,
    string CCompiler,
    string CxxCompiler,
    string CudaCompiler,
    string NvidiaSmi,
    string CmakeGenerator);
