using Alicia.Application.Providers;
using Alicia.Infrastructure.Providers;

namespace Alicia.Infrastructure.Tests.Providers;

public sealed class JsonInferenceProviderConfigurationStoreTests
{
    [Fact]
    public async Task StoreRoundTripsSelectedProviderAndExplicitOptions()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "providers", "configuration.json");
        using JsonInferenceProviderConfigurationStore store = new(path);
        InferenceProviderConfiguration configuration = new(
            "llama.cpp.cuda",
            "owner/model-GGUF:Q5_K_M",
            contextSize: 8192,
            generation: new InferenceGenerationOptions(
                maxOutputTokens: 256,
                temperature: 0.6,
                topP: 0.9,
                topK: 50,
                seed: 42));

        await store.SaveAsync(configuration, selectProvider: true, cancellationToken).ConfigureAwait(true);

        Assert.Equal(
            "llama.cpp.cuda",
            await store.LoadSelectedProviderIdAsync(cancellationToken).ConfigureAwait(true));
        Assert.Equal(
            configuration,
            await store.LoadAsync("llama.cpp.cuda", cancellationToken).ConfigureAwait(true));
        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hfToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreMigratesLegacyLot08ModelReferenceWithoutInventingOverrides()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string legacyPath = Path.Combine(directory.Path, "llama.cpp", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(
            legacyPath,
            "{\"ModelReference\":\" owner/model-GGUF:Q4_K_M \"}",
            cancellationToken).ConfigureAwait(true);
        using JsonInferenceProviderConfigurationStore store = new(
            Path.Combine(directory.Path, "configuration.json"),
            "llama.cpp.cuda",
            legacyPath);

        string? selectedProviderId = await store
            .LoadSelectedProviderIdAsync(cancellationToken)
            .ConfigureAwait(true);
        InferenceProviderConfiguration? configuration = await store
            .LoadAsync("llama.cpp.cuda", cancellationToken)
            .ConfigureAwait(true);

        Assert.Equal("llama.cpp.cuda", selectedProviderId);
        Assert.NotNull(configuration);
        Assert.Equal("owner/model-GGUF:Q4_K_M", configuration.ModelReference);
        Assert.True(configuration.UsesProviderDefaults);
    }

    [Fact]
    public async Task StoreRejectsUnknownSchemaInsteadOfSilentlyFallingBack()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.json");
        await File.WriteAllTextAsync(
            path,
            "{\"schemaVersion\":99,\"selectedProviderId\":null,\"providers\":{}}",
            cancellationToken)
            .ConfigureAwait(true);
        using JsonInferenceProviderConfigurationStore store = new(path);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadSelectedProviderIdAsync(cancellationToken)).ConfigureAwait(true);

        Assert.Contains("schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreCanPersistConfigurationWithoutSelectingIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        using JsonInferenceProviderConfigurationStore store = new(
            Path.Combine(directory.Path, "configuration.json"));
        InferenceProviderConfiguration configuration = new(
            "provider.optional",
            "model");

        await store.SaveAsync(configuration, selectProvider: false, cancellationToken).ConfigureAwait(true);

        Assert.Null(await store.LoadSelectedProviderIdAsync(cancellationToken).ConfigureAwait(true));
        Assert.Equal(
            configuration,
            await store.LoadAsync("provider.optional", cancellationToken).ConfigureAwait(true));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"alicia-provider-config-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
