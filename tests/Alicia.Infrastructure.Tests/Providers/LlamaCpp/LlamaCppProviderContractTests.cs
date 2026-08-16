using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Providers.LlamaCpp;

namespace Alicia.Infrastructure.Tests.Providers.LlamaCpp;

public sealed class LlamaCppProviderContractTests
{
    private const string TestApiKey = "A1B2C3D4E5F60718293A4B5C6D7E8F90";

    [Theory]
    [InlineData("ggml-org/gemma-3-1b-it-GGUF", "ggml-org/gemma-3-1b-it-GGUF")]
    [InlineData(" owner/model-GGUF:Q4_K_M ", "owner/model-GGUF:Q4_K_M")]
    public void ModelReferenceNormalizesSupportedHuggingFaceForm(
        string input,
        string expected)
    {
        Assert.Equal(expected, LlamaCppModelReference.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("model-only")]
    [InlineData("owner/")]
    [InlineData("owner/model: ")]
    [InlineData("owner/model with space")]
    [InlineData("owner//model")]
    [InlineData("owner/model:Q4:Q5")]
    [InlineData("owner/model:Q4/K")]
    public void ModelReferenceRejectsInvalidHuggingFaceForm(string input)
    {
        Assert.Throws<ArgumentException>(() => LlamaCppModelReference.Normalize(input));
    }

    [Fact]
    public void ServerCommandPassesModelDirectlyToHfWithoutOverridingGpuLayerDefaults()
    {
        string[] arguments = LlamaCppServerCommand.CreateArguments(
            "owner/model-GGUF:Q5_K_M",
            "/tmp/alicia-llama.log",
            port: 43123);

        AssertOption(arguments, "-hf", "owner/model-GGUF:Q5_K_M");
        AssertOption(arguments, "--host", "127.0.0.1");
        AssertOption(arguments, "--port", "43123");
        AssertOption(arguments, "--cors-origins", "localhost");
        Assert.Contains("--no-ui", arguments);
        Assert.DoesNotContain("--api-key", arguments);
        Assert.DoesNotContain("--n-gpu-layers", arguments);
        AssertOption(arguments, "--alias", "alicia-local");
        Assert.Contains("--jinja", arguments);
        Assert.Contains("--no-mmproj", arguments);
    }

    [Fact]
    public void ServerCommandAddsContextSizeOnlyWhenExplicitlyConfigured()
    {
        string[] providerDefaults = LlamaCppServerCommand.CreateArguments(
            "owner/model-GGUF",
            "/tmp/alicia-default.log",
            port: 43123);
        string[] configured = LlamaCppServerCommand.CreateArguments(
            "owner/model-GGUF",
            "/tmp/alicia-configured.log",
            port: 43124,
            contextSize: 8192);

        Assert.DoesNotContain("--ctx-size", providerDefaults);
        AssertOption(configured, "--ctx-size", "8192");
    }

    [Fact]
    public void ServerSecurityKeepsEphemeralApiKeyOutOfCommandLine()
    {
        string firstApiKey = LlamaCppServerSecurity.CreateEphemeralApiKey();
        string secondApiKey = LlamaCppServerSecurity.CreateEphemeralApiKey();
        ProcessStartInfo startInfo = new("llama-server");
        LlamaCppServerSecurity.ApplyApiKey(startInfo, firstApiKey);
        string[] arguments = LlamaCppServerCommand.CreateArguments(
            "owner/model-GGUF",
            "/tmp/alicia-secure.log",
            port: 43125);

        Assert.Equal(64, firstApiKey.Length);
        Assert.NotEqual(firstApiKey, secondApiKey);
        Assert.Equal(
            firstApiKey,
            startInfo.Environment[LlamaCppServerSecurity.ApiKeyEnvironmentVariable]);
        Assert.DoesNotContain(firstApiKey, arguments);
        Assert.DoesNotContain("--api-key", arguments);
    }

    [Fact]
    public async Task ServerSessionProbeRequiresProtectedEndpointAndMatchingCredential()
    {
        SessionOwnershipHttpMessageHandler handler = new(TestApiKey);
        using HttpClient httpClient = new(handler);

        await LlamaCppServerSessionProbe.VerifyOwnershipAsync(
            httpClient,
            new Uri("http://127.0.0.1:43125/"),
            TestApiKey,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, handler.AuthorizationParameters.Count);
        Assert.Null(handler.AuthorizationParameters[0]);
        Assert.Equal(TestApiKey, handler.AuthorizationParameters[1]);
        Assert.All(handler.RequestPaths, path => Assert.Equal("/props", path));
    }

    [Fact]
    public async Task IdempotentHttpRetryRetriesTransientStatusesAndThenSucceeds()
    {
        ScriptedHttpMessageHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        using HttpClient httpClient = new(handler);
        LlamaCppIdempotentHttpRetryPolicy retryPolicy = new(
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero);

        using HttpResponseMessage response = await retryPolicy.SendAsync(
            async token =>
            {
                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    "https://example.test/idempotent");
                return await httpClient.SendAsync(request, token).ConfigureAwait(false);
            },
            "The idempotent request failed.",
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task IdempotentHttpRetryDoesNotReplayPermanentResponses()
    {
        ScriptedHttpMessageHandler handler = new(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.OK);
        using HttpClient httpClient = new(handler);
        LlamaCppIdempotentHttpRetryPolicy retryPolicy = new(
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero);

        using HttpResponseMessage response = await retryPolicy.SendAsync(
            async token =>
            {
                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    "https://example.test/idempotent");
                return await httpClient.SendAsync(request, token).ConfigureAwait(false);
            },
            "The idempotent request failed.",
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task IdempotentHttpRetryRespectsCancellationDuringBackoff()
    {
        ScriptedHttpMessageHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        using HttpClient httpClient = new(handler);
        LlamaCppIdempotentHttpRetryPolicy retryPolicy = new(
            maxAttempts: 3,
            retryDelay: TimeSpan.FromSeconds(1));
        using CancellationTokenSource cancellationSource = new(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => retryPolicy.SendAsync(
                async token =>
                {
                    using HttpRequestMessage request = new(
                        HttpMethod.Get,
                        "https://example.test/idempotent");
                    return await httpClient.SendAsync(request, token).ConfigureAwait(false);
                },
                "The idempotent request failed.",
                cancellationSource.Token)).ConfigureAwait(true);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ServerSessionProbeRetriesOnlyTransientIdempotentResponses()
    {
        ScriptedHttpMessageHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        using HttpClient httpClient = new(handler);
        LlamaCppIdempotentHttpRetryPolicy retryPolicy = new(
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero);

        await LlamaCppServerSessionProbe.VerifyOwnershipAsync(
            httpClient,
            new Uri("http://127.0.0.1:43125/"),
            TestApiKey,
            retryPolicy,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(4, handler.CallCount);
        Assert.Equal(new string?[] { null, null, TestApiKey, TestApiKey }, handler.AuthorizationParameters);
    }

    [Fact]
    public async Task ServerSessionProbeRejectsEndpointWithoutAuthentication()
    {
        SessionOwnershipHttpMessageHandler handler = new(expectedApiKey: null);
        using HttpClient httpClient = new(handler);

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => LlamaCppServerSessionProbe.VerifyOwnershipAsync(
                httpClient,
                new Uri("http://127.0.0.1:43125/"),
                TestApiKey,
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Faulted, exception.Kind);
        Assert.Contains("session security", exception.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not enforce", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerSessionProbeRejectsDifferentAuthenticatedServer()
    {
        SessionOwnershipHttpMessageHandler handler = new(
            "00112233445566778899AABBCCDDEEFF");
        using HttpClient httpClient = new(handler);

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => LlamaCppServerSessionProbe.VerifyOwnershipAsync(
                httpClient,
                new Uri("http://127.0.0.1:43125/"),
                TestApiKey,
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Faulted, exception.Kind);
        Assert.Contains("credential", exception.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("401", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerConfiguresCudaServerWithHttpsModelDownloads()
    {
        LlamaCppBuildToolchain toolchain = new(
            "/usr/bin/cmake",
            "/usr/bin/gcc",
            "/usr/bin/g++",
            "/opt/cuda/bin/nvcc",
            "/usr/bin/nvidia-smi",
            "Ninja");

        IReadOnlyList<string> arguments = LlamaCppInstaller.CreateConfigureArguments(
            toolchain,
            "/tmp/llama-source",
            "/tmp/llama-build");

        Assert.Contains("-DGGML_CUDA=ON", arguments);
        Assert.Contains("-DLLAMA_BUILD_TOOLS=ON", arguments);
        Assert.Contains("-DLLAMA_BUILD_SERVER=ON", arguments);
        Assert.Contains("-DLLAMA_OPENSSL=ON", arguments);
        Assert.Contains("-DBUILD_SHARED_LIBS=OFF", arguments);
        Assert.DoesNotContain("-DLLAMA_BUILD_TOOLS=OFF", arguments);
        AssertOption(arguments, "-G", "Ninja");
        Assert.Contains("-DCMAKE_C_COMPILER=/usr/bin/gcc", arguments);
        Assert.Contains("-DCMAKE_CXX_COMPILER=/usr/bin/g++", arguments);
        Assert.Contains("-DCMAKE_CUDA_COMPILER=/opt/cuda/bin/nvcc", arguments);
    }

    [Theory]
    [InlineData("[1/20] Building CXX object", 0.05)]
    [InlineData("[ 75%] Linking CXX executable", 0.75)]
    public void InstallerParsesNinjaAndMakeProgress(
        string line,
        double expected)
    {
        Assert.True(LlamaCppInstaller.TryParseBuildFraction(line, out double fraction));
        Assert.Equal(expected, fraction, precision: 6);
    }

    [Theory]
    [InlineData("ninja: no work to do.")]
    [InlineData("Building llama-server")]
    [InlineData("[x/y] invalid")]
    public void InstallerIgnoresUnstructuredBuildProgress(string line)
    {
        Assert.False(LlamaCppInstaller.TryParseBuildFraction(line, out _));
    }

    [Fact]
    public void LatestReleaseParserRequiresHttpsTagAndTarball()
    {
        LlamaCppReleaseDescriptor descriptor = LlamaCppInstaller.ParseReleaseDescriptor(
            """
            {
              "tag_name": "b9999",
              "target_commitish": "0123456789abcdef0123456789abcdef01234567",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b9999"
            }
            """);

        Assert.Equal("b9999", descriptor.TagName);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", descriptor.TargetCommitish);
        Assert.Equal(Uri.UriSchemeHttps, descriptor.TarballUri.Scheme);

        Assert.Throws<InvalidDataException>(() =>
            LlamaCppInstaller.ParseReleaseDescriptor(
                "{\"tag_name\":\"b1\",\"target_commitish\":\"0123456789abcdef0123456789abcdef01234567\",\"tarball_url\":\"http://example.test/source\"}"));
        Assert.Throws<InvalidDataException>(() =>
            LlamaCppInstaller.ParseReleaseDescriptor(
                "{\"tag_name\":\"b1\",\"target_commitish\":\"0123456789abcdef0123456789abcdef01234567\",\"tarball_url\":\"https://example.test/source\"}"));
        Assert.Throws<InvalidDataException>(() =>
            LlamaCppInstaller.ParseReleaseDescriptor(
                "{\"tag_name\":\"../escape\",\"target_commitish\":\"0123456789abcdef0123456789abcdef01234567\",\"tarball_url\":\"https://api.github.com/repos/ggml-org/llama.cpp/tarball/b1\"}"));
    }

    [Fact]
    public void ReleasePolicyPinsValidatedTagCommitAndImmutableTarball()
    {
        Assert.Equal("b10435", LlamaCppReleasePolicy.ValidatedReleaseTag);
        Assert.Equal(
            "9e40df63ba151d771d8b247ac4011cf203337e99",
            LlamaCppReleasePolicy.ValidatedCommitSha);
        Assert.Equal(
            "/repos/ggml-org/llama.cpp/tarball/9e40df63ba151d771d8b247ac4011cf203337e99",
            LlamaCppReleasePolicy.ValidatedTarballUri.AbsolutePath);
        Assert.Equal(
            0,
            LlamaCppReleasePolicy.CompareReleaseTags("b10435", "b10435"));
        Assert.True(LlamaCppReleasePolicy.CompareReleaseTags("b10434", "b10435") < 0);
        Assert.True(LlamaCppReleasePolicy.CompareReleaseTags("b10436", "b10435") > 0);
        Assert.Throws<ArgumentException>(() => LlamaCppReleasePolicy.ParseReleaseSequence("latest"));
    }

    [Fact]
    public async Task ValidatedReleaseLookupRequiresPinnedGitCommit()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "b10435",
              "target_commitish": "9e40df63ba151d771d8b247ac4011cf203337e99",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b10435"
            }
            """);
        using HttpClient httpClient = new(handler);
        LlamaCppInstaller installer = new(httpClient, TimeProvider.System);

        LlamaCppReleaseDescriptor release = await installer.GetValidatedReleaseAsync(
            LlamaCppReleasePolicy.ValidatedReleaseTag,
            LlamaCppReleasePolicy.ValidatedCommitSha,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("b10435", release.TagName);
        Assert.Equal(LlamaCppReleasePolicy.ValidatedCommitSha, release.TargetCommitish);
        Assert.Equal(LlamaCppReleasePolicy.ValidatedTarballUri, release.TarballUri);
        Assert.Equal(
            "/repos/ggml-org/llama.cpp/releases/tags/b10435",
            handler.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task ValidatedReleaseLookupRejectsMovedReleaseTag()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "b10435",
              "target_commitish": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b10435"
            }
            """);
        using HttpClient httpClient = new(handler);
        LlamaCppInstaller installer = new(httpClient, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.GetValidatedReleaseAsync(
            LlamaCppReleasePolicy.ValidatedReleaseTag,
            LlamaCppReleasePolicy.ValidatedCommitSha,
            CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task RuntimeUpdateCheckComparesManagedValidatedAndLatestReleases()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-update-check-{Guid.NewGuid():N}");
        string releaseDirectory = Path.Combine(runtimeDirectory, "releases", "b10434");
        string executablePath = Path.Combine(releaseDirectory, "llama-server");
        Directory.CreateDirectory(releaseDirectory);
        await File.WriteAllTextAsync(executablePath, "test", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        LlamaCppInstallation installation = new(
            "b10434",
            executablePath,
            new DateTimeOffset(2026, 8, 14, 20, 0, 0, TimeSpan.Zero));
        await LlamaCppInstaller.WriteInstallationMetadataAsync(
            runtimeDirectory,
            installation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "b10435",
              "target_commitish": "9e40df63ba151d771d8b247ac4011cf203337e99",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b10435"
            }
            """);

        try
        {
            await using LlamaCppProviderRuntime runtime = new(
                runtimeDirectory,
                TimeProvider.System,
                LlamaCppProviderTimeouts.Default,
                handler);

            InferenceProviderUpdateInfo info = await runtime.CheckForUpdateAsync(
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal("b10434", info.InstalledVersion);
            Assert.Equal("b10435", info.ValidatedVersion);
            Assert.Equal("b10435", info.LatestVersion);
            Assert.True(info.IsManagedInstallation);
            Assert.True(info.IsUpdateAvailable);
            Assert.True(info.IsLatestVersionValidated);
            Assert.Contains("previous managed release will be kept", info.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeUpdateCheckNeverDowngradesManagedReleaseNewerThanValidated()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-update-no-downgrade-{Guid.NewGuid():N}");
        string releaseDirectory = Path.Combine(runtimeDirectory, "releases", "b10436");
        string executablePath = Path.Combine(releaseDirectory, "llama-server");
        Directory.CreateDirectory(releaseDirectory);
        await File.WriteAllTextAsync(executablePath, "test", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        LlamaCppInstallation installation = new(
            "b10436",
            executablePath,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
        await LlamaCppInstaller.WriteInstallationMetadataAsync(
            runtimeDirectory,
            installation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "b10437",
              "target_commitish": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b10437"
            }
            """);

        try
        {
            await using LlamaCppProviderRuntime runtime = new(
                runtimeDirectory,
                TimeProvider.System,
                LlamaCppProviderTimeouts.Default,
                handler);

            InferenceProviderUpdateInfo info = await runtime.CheckForUpdateAsync(
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal("b10436", info.InstalledVersion);
            Assert.Equal("b10435", info.ValidatedVersion);
            Assert.Equal("b10437", info.LatestVersion);
            Assert.False(info.IsUpdateAvailable);
            Assert.False(info.IsLatestVersionValidated);
            Assert.Contains("will not downgrade", info.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeUpdateCheckReportsNewerUpstreamReleaseWithoutTrustingIt()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-update-unvalidated-latest-{Guid.NewGuid():N}");
        string releaseDirectory = Path.Combine(runtimeDirectory, "releases", "b10435");
        string executablePath = Path.Combine(releaseDirectory, "llama-server");
        Directory.CreateDirectory(releaseDirectory);
        await File.WriteAllTextAsync(executablePath, "test", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        LlamaCppInstallation installation = new(
            "b10435",
            executablePath,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
        await LlamaCppInstaller.WriteInstallationMetadataAsync(
            runtimeDirectory,
            installation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "b10436",
              "target_commitish": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b10436"
            }
            """);

        try
        {
            await using LlamaCppProviderRuntime runtime = new(
                runtimeDirectory,
                TimeProvider.System,
                LlamaCppProviderTimeouts.Default,
                handler);

            InferenceProviderUpdateInfo info = await runtime.CheckForUpdateAsync(
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.False(info.IsUpdateAvailable);
            Assert.False(info.IsLatestVersionValidated);
            Assert.Equal("b10436", info.LatestVersion);
            Assert.Contains("has not been validated", info.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeUpdateCheckDoesNotTrustMovedValidatedLatestReleaseTag()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-update-moved-latest-{Guid.NewGuid():N}");
        string releaseDirectory = Path.Combine(runtimeDirectory, "releases", "b10435");
        string executablePath = Path.Combine(releaseDirectory, "llama-server");
        Directory.CreateDirectory(releaseDirectory);
        await File.WriteAllTextAsync(executablePath, "test", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        LlamaCppInstallation installation = new(
            "b10435",
            executablePath,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
        await LlamaCppInstaller.WriteInstallationMetadataAsync(
            runtimeDirectory,
            installation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "b10435",
              "target_commitish": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b10435"
            }
            """);

        try
        {
            await using LlamaCppProviderRuntime runtime = new(
                runtimeDirectory,
                TimeProvider.System,
                LlamaCppProviderTimeouts.Default,
                handler);

            InferenceProviderUpdateInfo info = await runtime.CheckForUpdateAsync(
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.False(info.IsUpdateAvailable);
            Assert.False(info.IsLatestVersionValidated);
            Assert.Equal("b10435", info.LatestVersion);
            Assert.Contains("has not been validated", info.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeUpdateCheckSanitizesInvalidManagedReleaseMetadata()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-update-invalid-metadata-{Guid.NewGuid():N}");
        string releaseDirectory = Path.Combine(runtimeDirectory, "releases", "legacy");
        string executablePath = Path.Combine(releaseDirectory, "llama-server");
        Directory.CreateDirectory(releaseDirectory);
        await File.WriteAllTextAsync(executablePath, "test", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        LlamaCppInstallation installation = new(
            "legacy-version",
            executablePath,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
        await LlamaCppInstaller.WriteInstallationMetadataAsync(
            runtimeDirectory,
            installation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "b10435",
              "target_commitish": "9e40df63ba151d771d8b247ac4011cf203337e99",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b10435"
            }
            """);

        try
        {
            await using LlamaCppProviderRuntime runtime = new(
                runtimeDirectory,
                TimeProvider.System,
                LlamaCppProviderTimeouts.Default,
                handler);

            InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
                () => runtime.CheckForUpdateAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);

            Assert.Equal(InferenceProviderFailureKind.Faulted, exception.Kind);
            Assert.Equal(
                "Alicia could not complete the managed llama.cpp update. The previous managed release remains selected.",
                exception.UserMessage);
            Assert.DoesNotContain("legacy-version", exception.UserMessage, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InstallationMetadataCancellationPreservesPreviousActiveRelease()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-update-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtimeDirectory);
        LlamaCppInstallation previous = new(
            "b10434",
            Path.Combine(runtimeDirectory, "releases", "b10434", "llama-server"),
            new DateTimeOffset(2026, 8, 14, 20, 0, 0, TimeSpan.Zero));
        LlamaCppInstallation candidate = new(
            "b10435",
            Path.Combine(runtimeDirectory, "releases", "b10435", "llama-server"),
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

        try
        {
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                previous,
                CancellationToken.None).ConfigureAwait(true);
            using CancellationTokenSource cancellationSource = new();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                LlamaCppInstaller.WriteInstallationMetadataAsync(
                    runtimeDirectory,
                    candidate,
                    cancellationSource.Token)).ConfigureAwait(true);

            string json = await File.ReadAllTextAsync(
                Path.Combine(runtimeDirectory, "installation.json"),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal("b10434", document.RootElement.GetProperty("Version").GetString());
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "Available devices:\n  CUDA0: NVIDIA GeForce RTX 4070 (8192 MiB, 7000 MiB free)",
        true)]
    [InlineData("Available devices:\n  (none)", false)]
    [InlineData("Available devices:\n  Vulkan0: NVIDIA GeForce RTX 4070", false)]
    public void RuntimeProbeRecognizesActiveCudaBackend(string output, bool expected)
    {
        Assert.Equal(expected, LlamaCppProviderRuntime.OutputShowsCuda(output));
    }

    [Fact]
    public void RuntimeProbeParsesVersionLine()
    {
        Assert.Equal(
            "9637 (`abc`)",
            LlamaCppProviderRuntime.ParseVersion(
                "ggml_cuda_init: found 1 CUDA devices:\nversion: 9637 (`abc`)\nbuilt with GNU"));
    }

    [Fact]
    public async Task ChatClientStreamsOpenAiCompatibleContentDeltas()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            data: {"choices":[{"delta":{"role":"assistant"}}]}

            data: {"choices":[{"delta":{"content":"Hello"}}]}

            data: {"choices":[{"delta":{"content":" from CUDA"}}]}

            data: [DONE]

            """);
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);
        ConversationResponseRequest request = CreateRequest();
        List<string> chunks = [];

        await foreach (ConversationResponseChunk chunk in client
            .StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                request,
                new InferenceGenerationOptions(),
                CancellationToken.None)
            .ConfigureAwait(true))
        {
            chunks.Add(chunk.ContentDelta);
        }

        Assert.Equal("Hello from CUDA", string.Concat(chunks));
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(TestApiKey, handler.AuthorizationParameter);
        string requestBody = Assert.IsType<string>(handler.RequestBody);
        Assert.DoesNotContain(TestApiKey, requestBody, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(
            "alicia-local",
            document.RootElement.GetProperty("model").GetString());
        Assert.False(document.RootElement.TryGetProperty("max_tokens", out _));
        Assert.False(document.RootElement.TryGetProperty("temperature", out _));
        Assert.False(document.RootElement.TryGetProperty("top_p", out _));
        Assert.False(document.RootElement.TryGetProperty("top_k", out _));
        Assert.False(document.RootElement.TryGetProperty("seed", out _));
        Assert.False(document.RootElement.TryGetProperty("reasoning_effort", out _));
        Assert.False(document.RootElement.TryGetProperty("thinking_budget_tokens", out _));
        Assert.False(document.RootElement.TryGetProperty("reasoning_format", out _));
        JsonElement messages = document.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ChatClientNeverAutomaticallyRetriesAmbiguousGenerationPost()
    {
        ScriptedHttpMessageHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Network, exception.Kind);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ChatClientSerializesOnlyExplicitGenerationOverrides()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);
        InferenceGenerationOptions options = new(
            maxOutputTokens: 128,
            temperature: 0.55,
            topP: 0.92,
            topK: 32,
            seed: 1234);

        await ConsumeAsync(client.StreamAsync(
            new Uri("http://127.0.0.1:8080/"),
            TestApiKey,
            CreateRequest(),
            options,
            CancellationToken.None)).ConfigureAwait(true);

        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        Assert.Equal(128, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.55, root.GetProperty("temperature").GetDouble());
        Assert.Equal(0.92, root.GetProperty("top_p").GetDouble());
        Assert.Equal(32, root.GetProperty("top_k").GetInt32());
        Assert.Equal(1234, root.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task ChatClientStreamsReasoningAndContentDeltasSeparately()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            data: {"choices":[{"delta":{"reasoning_content":"Inspect premise"}}]}

            data: {"choices":[{"delta":{"reasoning_content":" then verify","content":"Final answer"}}]}

            data: [DONE]

            """);
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);
        List<ConversationResponseChunk> chunks = [];

        await foreach (ConversationResponseChunk chunk in client
            .StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(
                    reasoningEnabled: true,
                    reasoningBudgetTokens: 512),
                CancellationToken.None)
            .ConfigureAwait(true))
        {
            chunks.Add(chunk);
        }

        Assert.Collection(
            chunks,
            chunk =>
            {
                Assert.Equal(ConversationResponseChunkKind.Reasoning, chunk.Kind);
                Assert.Equal("Inspect premise", chunk.TextDelta);
            },
            chunk =>
            {
                Assert.Equal(ConversationResponseChunkKind.Reasoning, chunk.Kind);
                Assert.Equal(" then verify", chunk.TextDelta);
            },
            chunk =>
            {
                Assert.Equal(ConversationResponseChunkKind.Content, chunk.Kind);
                Assert.Equal("Final answer", chunk.TextDelta);
            });
    }

    [Fact]
    public async Task ChatClientDisablesReasoningExplicitlyWhenConfiguredOff()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        await ConsumeAsync(client.StreamAsync(
            new Uri("http://127.0.0.1:8080/"),
            TestApiKey,
            CreateRequest(),
            new InferenceGenerationOptions(reasoningEnabled: false),
            CancellationToken.None)).ConfigureAwait(true);

        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal(0, root.GetProperty("thinking_budget_tokens").GetInt32());
        Assert.False(root.TryGetProperty("reasoning_format", out _));
    }

    [Fact]
    public async Task ChatClientSendsBoundedSeparatedReasoningWhenConfiguredOn()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        await ConsumeAsync(client.StreamAsync(
            new Uri("http://127.0.0.1:8080/"),
            TestApiKey,
            CreateRequest(),
            new InferenceGenerationOptions(
                reasoningEnabled: true,
                reasoningBudgetTokens: 384),
            CancellationToken.None)).ConfigureAwait(true);

        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        Assert.Equal(384, root.GetProperty("thinking_budget_tokens").GetInt32());
        Assert.Equal("deepseek", root.GetProperty("reasoning_format").GetString());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task ChatClientRejectsTruncatedStreamSoPartialTextCannotBePersisted()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Faulted, exception.Kind);
        Assert.Contains("ended unexpectedly", exception.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[DONE]", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientClassifiesMalformedSseJsonAsFaulted()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: {not-json}\n\ndata: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Faulted, exception.Kind);
        Assert.Contains("invalid response", exception.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("malformed JSON", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientClassifiesServerUnavailableWithoutLeakingResponseBody()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            "{\"error\":{\"message\":\"Loading /home/user/private-model.gguf token=secret\"}}");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Network, exception.Kind);
        Assert.Contains("temporarily unavailable", exception.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/user", exception.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", exception.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("503", exception.UserMessage, StringComparison.Ordinal);
        InvalidOperationException diagnostic = Assert.IsType<InvalidOperationException>(
            exception.InnerException);
        Assert.Contains("503", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientClassifiesRejectedModelRequestWithoutLeakingResponseBody()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.BadRequest,
            "model /home/user/private-model.gguf failed: secret-detail");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Model, exception.Kind);
        Assert.Contains("model", exception.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/user", exception.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-detail", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientBoundsResponseHeaderWaitAndClassifiesTimeoutAsNetwork()
    {
        using HttpClient httpClient = new(new HangingHttpMessageHandler());
        LlamaCppChatClient client = new(
            httpClient,
            new LlamaCppProviderTimeouts(
                chatResponseHeaders: TimeSpan.FromMilliseconds(25)));

        InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Network, exception.Kind);
        Assert.Contains("did not respond in time", exception.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatClientPreservesCallerCancellationInsteadOfReportingTimeout()
    {
        using HttpClient httpClient = new(new HangingHttpMessageHandler());
        LlamaCppChatClient client = new(
            httpClient,
            new LlamaCppProviderTimeouts(
                chatResponseHeaders: TimeSpan.FromSeconds(2)));
        using CancellationTokenSource cancellationSource = new(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                TestApiKey,
                CreateRequest(),
                new InferenceGenerationOptions(),
                cancellationSource.Token))).ConfigureAwait(true);
    }

    [Fact]
    public void RuntimeStartupFailureKeepsRawLogTailOutOfUserMessage()
    {
        InferenceProviderException exception = LlamaCppProviderRuntime.CreateStartupExitFailure(
            1,
            " Last log lines: failed to load model /home/user/private.gguf token=secret");

        Assert.Equal(InferenceProviderFailureKind.Model, exception.Kind);
        Assert.DoesNotContain("/home/user", exception.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", exception.UserMessage, StringComparison.Ordinal);
        InvalidOperationException diagnostic = Assert.IsType<InvalidOperationException>(
            exception.InnerException);
        Assert.Contains("/home/user", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutGuardBoundsOperationWithoutReclassifyingCallerCancellation()
    {
        InferenceProviderException timeout = await Assert.ThrowsAsync<InferenceProviderException>(
            () => LlamaCppTimeoutGuard.RunAsync(
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    return true;
                },
                TimeSpan.FromMilliseconds(25),
                InferenceProviderFailureKind.Model,
                "The model did not become ready in time.",
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(InferenceProviderFailureKind.Model, timeout.Kind);

        using CancellationTokenSource cancellationSource = new(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LlamaCppTimeoutGuard.RunAsync(
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    return true;
                },
                TimeSpan.FromSeconds(2),
                InferenceProviderFailureKind.Model,
                "The model did not become ready in time.",
                cancellationSource.Token)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("error: failed to load model owner/model-GGUF", true)]
    [InlineData("CUDA error: out of memory while allocating tensors", true)]
    [InlineData("fatal: unexpected socket shutdown", false)]
    public void RuntimeClassifiesStartupDiagnosticsWithoutExposingThem(
        string diagnostic,
        bool expectedModelFailure)
    {
        Assert.Equal(
            expectedModelFailure,
            LlamaCppProviderRuntime.LooksLikeModelStartupFailure(diagnostic));
    }

    [Fact]
    public async Task ManagedStorageInspectionSeparatesRuntimeAndModelCacheBytes()
    {
        string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-inspect-{Guid.NewGuid():N}");
        string runtimeDirectory = Path.Combine(rootDirectory, "providers", "llama.cpp");
        string activeExecutable = Path.Combine(runtimeDirectory, "releases", "b10435", "llama-server");
        string oldExecutable = Path.Combine(runtimeDirectory, "releases", "b10434", "llama-server");
        string modelFile = Path.Combine(runtimeDirectory, "models", "model.gguf");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(activeExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(oldExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(modelFile)!);
            await File.WriteAllBytesAsync(
                activeExecutable,
                new byte[128],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                oldExecutable,
                new byte[64],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                modelFile,
                new byte[512],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                new LlamaCppInstallation(
                    "b10435",
                    activeExecutable,
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            InferenceProviderStorageInfo info = await runtime.InspectStorageAsync(
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.True(info.HasManagedRuntime);
            Assert.Equal("b10435", info.ManagedVersion);
            Assert.Equal(1, info.RetainedReleaseCount);
            Assert.True(info.RuntimeBytes >= 192);
            Assert.Equal(512, info.ModelCacheBytes);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeOnlyUninstallPreservesModelCacheConfigurationAndConversations()
    {
        string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-runtime-only-{Guid.NewGuid():N}");
        string providersDirectory = Path.Combine(rootDirectory, "providers");
        string runtimeDirectory = Path.Combine(providersDirectory, "llama.cpp");
        string activeExecutable = Path.Combine(runtimeDirectory, "releases", "b10435", "llama-server");
        string modelFile = Path.Combine(runtimeDirectory, "models", "model.gguf");
        string configurationFile = Path.Combine(providersDirectory, "configuration.json");
        string conversationFile = Path.Combine(rootDirectory, "conversations", "conversation.json");
        string legacySettingsFile = Path.Combine(runtimeDirectory, "settings.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(activeExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(modelFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(configurationFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(conversationFile)!);
            Directory.CreateDirectory(Path.Combine(runtimeDirectory, "logs"));
            Directory.CreateDirectory(Path.Combine(runtimeDirectory, ".staging", "stale"));
            await File.WriteAllTextAsync(activeExecutable, "runtime", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(modelFile, "cached-model", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(configurationFile, "configuration", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(conversationFile, "conversation", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(legacySettingsFile, "legacy-settings", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(
                Path.Combine(runtimeDirectory, "logs", "server.log"),
                "log",
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                new LlamaCppInstallation(
                    "b10435",
                    activeExecutable,
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            InferenceProviderMaintenanceResult result = await runtime.UninstallAsync(
                InferenceProviderRemovalMode.RuntimeOnly,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(InferenceProviderState.Missing, result.Snapshot.State);
            Assert.False(result.StorageInfo.HasManagedRuntime);
            Assert.Equal(0, result.StorageInfo.RuntimeBytes);
            Assert.True(result.StorageInfo.ModelCacheBytes > 0);
            Assert.False(File.Exists(Path.Combine(runtimeDirectory, "installation.json")));
            Assert.False(Directory.Exists(Path.Combine(runtimeDirectory, "releases")));
            Assert.False(Directory.Exists(Path.Combine(runtimeDirectory, ".staging")));
            Assert.False(Directory.Exists(Path.Combine(runtimeDirectory, "logs")));
            Assert.True(File.Exists(modelFile));
            Assert.True(File.Exists(legacySettingsFile));
            Assert.True(File.Exists(configurationFile));
            Assert.True(File.Exists(conversationFile));
            Assert.Contains("model cache", result.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeAndModelCacheUninstallRemovesOnlyConfirmedProviderScopes()
    {
        string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-full-{Guid.NewGuid():N}");
        string providersDirectory = Path.Combine(rootDirectory, "providers");
        string runtimeDirectory = Path.Combine(providersDirectory, "llama.cpp");
        string activeExecutable = Path.Combine(runtimeDirectory, "releases", "b10435", "llama-server");
        string modelFile = Path.Combine(runtimeDirectory, "models", "model.gguf");
        string configurationFile = Path.Combine(providersDirectory, "configuration.json");
        string conversationFile = Path.Combine(rootDirectory, "conversations", "conversation.json");
        string legacySettingsFile = Path.Combine(runtimeDirectory, "settings.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(activeExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(modelFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(configurationFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(conversationFile)!);
            await File.WriteAllTextAsync(activeExecutable, "runtime", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(modelFile, "cached-model", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(configurationFile, "configuration", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(conversationFile, "conversation", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(legacySettingsFile, "legacy-settings", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                new LlamaCppInstallation(
                    "b10435",
                    activeExecutable,
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            InferenceProviderMaintenanceResult result = await runtime.UninstallAsync(
                InferenceProviderRemovalMode.RuntimeAndModelCache,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(InferenceProviderState.Missing, result.Snapshot.State);
            Assert.Equal(0, result.StorageInfo.RuntimeBytes);
            Assert.Equal(0, result.StorageInfo.ModelCacheBytes);
            Assert.False(Directory.Exists(Path.Combine(runtimeDirectory, "releases")));
            Assert.False(Directory.Exists(Path.Combine(runtimeDirectory, "models")));
            Assert.True(File.Exists(legacySettingsFile));
            Assert.True(File.Exists(configurationFile));
            Assert.True(File.Exists(conversationFile));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RetainedReleaseCleanupPreservesActiveCacheAndUnknownDirectories()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-clean-releases-{Guid.NewGuid():N}");
        string activeExecutable = Path.Combine(runtimeDirectory, "releases", "b10435", "llama-server");
        string oldExecutable = Path.Combine(runtimeDirectory, "releases", "b10434", "llama-server");
        string unknownExecutable = Path.Combine(runtimeDirectory, "releases", "custom-build", "llama-server");
        string modelFile = Path.Combine(runtimeDirectory, "models", "model.gguf");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(activeExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(oldExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(unknownExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(modelFile)!);
            await File.WriteAllTextAsync(activeExecutable, "active", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(oldExecutable, "old", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(unknownExecutable, "unknown", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(modelFile, "model", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                new LlamaCppInstallation(
                    "b10435",
                    activeExecutable,
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            InferenceProviderMaintenanceResult result = await runtime.CleanupRetainedReleasesAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(InferenceProviderState.Ready, result.Snapshot.State);
            Assert.True(result.StorageInfo.HasManagedRuntime);
            Assert.Equal("b10435", result.StorageInfo.ManagedVersion);
            Assert.Equal(0, result.StorageInfo.RetainedReleaseCount);
            Assert.True(File.Exists(activeExecutable));
            Assert.False(Directory.Exists(Path.GetDirectoryName(oldExecutable)!));
            Assert.True(File.Exists(unknownExecutable));
            Assert.True(File.Exists(modelFile));
            Assert.True(result.ReclaimedBytes > 0);
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MaintenanceRejectsMetadataThatPointsOutsideManagedRuntime()
    {
        string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-outside-metadata-{Guid.NewGuid():N}");
        string runtimeDirectory = Path.Combine(rootDirectory, "providers", "llama.cpp");
        string externalExecutable = Path.Combine(rootDirectory, "outside", "llama-server");
        string retainedRelease = Path.Combine(runtimeDirectory, "releases", "b10434", "llama-server");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(externalExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(retainedRelease)!);
            await File.WriteAllTextAsync(externalExecutable, "outside", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(retainedRelease, "retained", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                new LlamaCppInstallation(
                    "b10435",
                    externalExecutable,
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            InferenceProviderStorageInfo info = await runtime.InspectStorageAsync(
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            InferenceProviderException exception = await Assert.ThrowsAsync<InferenceProviderException>(
                () => runtime.CleanupRetainedReleasesAsync(
                    cancellationToken: TestContext.Current.CancellationToken)).ConfigureAwait(true);

            Assert.False(info.HasManagedRuntime);
            Assert.Equal(InferenceProviderFailureKind.Missing, exception.Kind);
            Assert.True(File.Exists(externalExecutable));
            Assert.True(File.Exists(retainedRelease));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeUninstallDoesNotFollowDirectorySymlinksOutsideManagedStorage()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-symlink-{Guid.NewGuid():N}");
        string runtimeDirectory = Path.Combine(rootDirectory, "providers", "llama.cpp");
        string activeExecutable = Path.Combine(runtimeDirectory, "releases", "b10435", "llama-server");
        string externalDirectory = Path.Combine(rootDirectory, "outside");
        string externalSentinel = Path.Combine(externalDirectory, "keep.txt");
        string logsDirectory = Path.Combine(runtimeDirectory, "logs");
        string linkedDirectory = Path.Combine(logsDirectory, "external-link");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(activeExecutable)!);
            Directory.CreateDirectory(externalDirectory);
            Directory.CreateDirectory(logsDirectory);
            await File.WriteAllTextAsync(activeExecutable, "runtime", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(externalSentinel, "keep", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            Directory.CreateSymbolicLink(linkedDirectory, externalDirectory);
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                new LlamaCppInstallation(
                    "b10435",
                    activeExecutable,
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            await runtime.UninstallAsync(
                InferenceProviderRemovalMode.RuntimeOnly,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.True(File.Exists(externalSentinel));
            Assert.False(Directory.Exists(logsDirectory));
        }
        finally
        {
            DirectoryInfo link = new(linkedDirectory);

            if (link.Exists && (link.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                link.Delete();
            }

            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CancelledMaintenanceDoesNotMutateManagedStorage()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-cancel-{Guid.NewGuid():N}");
        string activeExecutable = Path.Combine(runtimeDirectory, "releases", "b10435", "llama-server");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(activeExecutable)!);
            await File.WriteAllTextAsync(activeExecutable, "runtime", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            await LlamaCppInstaller.WriteInstallationMetadataAsync(
                runtimeDirectory,
                new LlamaCppInstallation(
                    "b10435",
                    activeExecutable,
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            using CancellationTokenSource cancellationSource = new();
            cancellationSource.Cancel();

            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.UninstallAsync(
                InferenceProviderRemovalMode.RuntimeOnly,
                cancellationToken: cancellationSource.Token)).ConfigureAwait(true);

            Assert.True(File.Exists(activeExecutable));
            Assert.True(File.Exists(Path.Combine(runtimeDirectory, "installation.json")));
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeUninstallRejectsUnknownRemovalModeBeforeMutation()
    {
        string runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"alicia-maintenance-invalid-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtimeDirectory);
        string sentinel = Path.Combine(runtimeDirectory, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        try
        {
            await using LlamaCppProviderRuntime runtime = new(runtimeDirectory);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runtime.UninstallAsync(
                (InferenceProviderRemovalMode)999,
                cancellationToken: TestContext.Current.CancellationToken)).ConfigureAwait(true);

            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    private static ConversationResponseRequest CreateRequest()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 20, 0, 0, TimeSpan.Zero);
        return new ConversationResponseRequest(
            ConversationId.New(),
            "Local model",
            [
                new ChatMessage(
                    MessageId.New(),
                    MessageRole.System,
                    "Be concise.",
                    createdAt),
                new ChatMessage(
                    MessageId.New(),
                    MessageRole.User,
                    "Hello",
                    createdAt.AddMinutes(1)),
            ]);
    }

    private static void AssertOption(
        IReadOnlyList<string> arguments,
        string option,
        string expectedValue)
    {
        int index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0);
        Assert.True(index + 1 < arguments.Count);
        Assert.Equal(expectedValue, arguments[index + 1]);
    }

    private static async Task ConsumeAsync(
        IAsyncEnumerable<ConversationResponseChunk> stream)
    {
        await foreach (ConversationResponseChunk _ in stream.ConfigureAwait(true))
        {
        }
    }

    private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;

        public ScriptedHttpMessageHandler(params HttpStatusCode[] statuses)
        {
            ArgumentNullException.ThrowIfNull(statuses);

            if (statuses.Length == 0)
            {
                throw new ArgumentException("At least one status is required.", nameof(statuses));
            }

            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        public int CallCount { get; private set; }

        public List<string?> AuthorizationParameters { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);

            HttpStatusCode statusCode = _statuses.Count > 1
                ? _statuses.Dequeue()
                : _statuses.Peek();

            string mediaType = statusCode == HttpStatusCode.OK
                ? "text/event-stream"
                : "application/json";
            string body = statusCode == HttpStatusCode.OK
                ? "data: [DONE]\n\n"
                : "{}";

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
                RequestMessage = request,
            });
        }
    }

    private sealed class SessionOwnershipHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _expectedApiKey;

        public SessionOwnershipHttpMessageHandler(string? expectedApiKey)
        {
            _expectedApiKey = expectedApiKey;
        }

        public List<string?> AuthorizationParameters { get; } = [];

        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? authorization = request.Headers.Authorization?.Parameter;
            AuthorizationParameters.Add(authorization);
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);

            HttpStatusCode statusCode = _expectedApiKey is null
                || string.Equals(authorization, _expectedApiKey, StringComparison.Ordinal)
                    ? HttpStatusCode.OK
                    : HttpStatusCode.Unauthorized;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }

    private sealed class HangingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public CapturingHttpMessageHandler(
            HttpStatusCode statusCode,
            string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public string? RequestBody { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    "text/event-stream"),
                RequestMessage = request,
            };
        }
    }
}
