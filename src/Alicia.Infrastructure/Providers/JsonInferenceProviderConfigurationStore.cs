using System.Text.Json;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers;

public sealed class JsonInferenceProviderConfigurationStore :
    IInferenceProviderConfigurationStore,
    IDisposable
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _configurationPath;
    private readonly string? _legacyProviderId;
    private readonly string? _legacySettingsPath;
    private bool _disposed;

    public JsonInferenceProviderConfigurationStore(
        string configurationPath,
        string? legacyProviderId = null,
        string? legacySettingsPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        if ((legacyProviderId is null) != (legacySettingsPath is null))
        {
            throw new ArgumentException(
                "Legacy provider identifier and settings path must either both be supplied or both be omitted.");
        }

        _configurationPath = Path.GetFullPath(configurationPath);
        _legacyProviderId = NormalizeOptional(legacyProviderId);
        _legacySettingsPath = legacySettingsPath is null
            ? null
            : Path.GetFullPath(legacySettingsPath);
    }

    public async Task<string?> LoadSelectedProviderIdAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ConfigurationDocument? document = await ReadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);

            if (document is not null)
            {
                return NormalizeOptional(document.SelectedProviderId);
            }

            LegacyProviderSettings? legacySettings = await ReadLegacySettingsAsync(cancellationToken)
                .ConfigureAwait(false);

            return legacySettings is null ? null : _legacyProviderId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InferenceProviderConfiguration?> LoadAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalizedProviderId = NormalizeRequiredProviderId(providerId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ConfigurationDocument? document = await ReadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);

            if (document is not null)
            {
                if (!document.Providers.TryGetValue(
                    normalizedProviderId,
                    out ProviderConfigurationDocument? stored))
                {
                    return null;
                }

                return MapToConfiguration(normalizedProviderId, stored);
            }

            if (!string.Equals(
                normalizedProviderId,
                _legacyProviderId,
                StringComparison.Ordinal))
            {
                return null;
            }

            LegacyProviderSettings? legacySettings = await ReadLegacySettingsAsync(cancellationToken)
                .ConfigureAwait(false);

            return legacySettings is null
                ? null
                : new InferenceProviderConfiguration(
                    normalizedProviderId,
                    legacySettings.ModelReference);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        InferenceProviderConfiguration configuration,
        bool selectProvider,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ConfigurationDocument document = await ReadDocumentAsync(cancellationToken)
                    .ConfigureAwait(false)
                ?? new ConfigurationDocument(
                    CurrentSchemaVersion,
                    SelectedProviderId: null,
                    new Dictionary<string, ProviderConfigurationDocument>(StringComparer.Ordinal));

            Dictionary<string, ProviderConfigurationDocument> providers = new(
                document.Providers,
                StringComparer.Ordinal)
            {
                [configuration.ProviderId] = MapFromConfiguration(configuration),
            };

            string? selectedProviderId = selectProvider
                ? configuration.ProviderId
                : NormalizeOptional(document.SelectedProviderId);
            ConfigurationDocument updated = new(
                CurrentSchemaVersion,
                selectedProviderId,
                providers);

            await WriteDocumentAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private async Task<ConfigurationDocument?> ReadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_configurationPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(
                _configurationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            ConfigurationDocument? document = await JsonSerializer
                .DeserializeAsync<ConfigurationDocument>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                throw new InvalidDataException(
                    "Provider configuration document is empty.");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported provider configuration schema version '{document.SchemaVersion}'.");
            }

            if (document.Providers is null)
            {
                throw new InvalidDataException(
                    "Provider configuration document does not contain a provider map.");
            }

            return document with
            {
                Providers = new Dictionary<string, ProviderConfigurationDocument>(
                    document.Providers,
                    StringComparer.Ordinal),
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Provider configuration JSON is invalid.",
                exception);
        }
    }

    private async Task<LegacyProviderSettings?> ReadLegacySettingsAsync(
        CancellationToken cancellationToken)
    {
        if (_legacySettingsPath is null || !File.Exists(_legacySettingsPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(
                _legacySettingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            LegacyProviderSettings? settings = await JsonSerializer
                .DeserializeAsync<LegacyProviderSettings>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (settings is null || string.IsNullOrWhiteSpace(settings.ModelReference))
            {
                return null;
            }

            return settings with { ModelReference = settings.ModelReference.Trim() };
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

    private async Task WriteDocumentAsync(
        ConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_configurationPath)
            ?? throw new InvalidOperationException(
                "Provider configuration path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_configurationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer
                    .SerializeAsync(stream, document, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _configurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static InferenceProviderConfiguration MapToConfiguration(
        string providerId,
        ProviderConfigurationDocument stored)
    {
        try
        {
            return new InferenceProviderConfiguration(
                providerId,
                stored.ModelReference,
                stored.ContextSize,
                new InferenceGenerationOptions(
                    stored.Generation?.MaxOutputTokens,
                    stored.Generation?.Temperature,
                    stored.Generation?.TopP,
                    stored.Generation?.TopK,
                    stored.Generation?.Seed,
                    stored.Generation?.ReasoningEnabled,
                    stored.Generation?.ReasoningBudgetTokens));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Stored configuration for provider '{providerId}' is invalid.",
                exception);
        }
    }

    private static ProviderConfigurationDocument MapFromConfiguration(
        InferenceProviderConfiguration configuration)
    {
        return new ProviderConfigurationDocument(
            configuration.ModelReference,
            configuration.ContextSize,
            new GenerationOptionsDocument(
                configuration.Generation.MaxOutputTokens,
                configuration.Generation.Temperature,
                configuration.Generation.TopP,
                configuration.Generation.TopK,
                configuration.Generation.Seed,
                configuration.Generation.ReasoningEnabled,
                configuration.Generation.ReasoningBudgetTokens));
    }

    private static string NormalizeRequiredProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException(
                "Provider identifier cannot be empty.",
                nameof(providerId));
        }

        return providerId.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ConfigurationDocument(
        int SchemaVersion,
        string? SelectedProviderId,
        Dictionary<string, ProviderConfigurationDocument> Providers);

    private sealed record ProviderConfigurationDocument(
        string? ModelReference,
        int? ContextSize,
        GenerationOptionsDocument? Generation);

    private sealed record GenerationOptionsDocument(
        int? MaxOutputTokens,
        double? Temperature,
        double? TopP,
        int? TopK,
        int? Seed,
        bool? ReasoningEnabled,
        int? ReasoningBudgetTokens);

    private sealed record LegacyProviderSettings(string ModelReference);
}
