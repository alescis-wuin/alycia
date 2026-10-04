using System.Globalization;
using Alicia.Application.Providers;

namespace Alicia.Presentation.ViewModels;

public sealed partial class ProviderViewModel
{
    public string WorkspaceGenerationOutcome => _providerGenerationObservation?.Outcome switch
    {
        InferenceProviderGenerationOutcome.Completed => "Completed",
        InferenceProviderGenerationOutcome.Cancelled => "Cancelled",
        InferenceProviderGenerationOutcome.Failed => "Failed",
        _ => "No generation yet",
    };

    public bool WorkspaceGenerationCompleted => _providerGenerationObservation?.Outcome == InferenceProviderGenerationOutcome.Completed;

    public bool WorkspaceGenerationCancelled => _providerGenerationObservation?.Outcome == InferenceProviderGenerationOutcome.Cancelled;

    public bool WorkspaceGenerationFailed => _providerGenerationObservation?.Outcome == InferenceProviderGenerationOutcome.Failed;

    public string WorkspaceGenerationFailure => _providerGenerationObservation?.FailureKind?.ToString() ?? string.Empty;

    public string WorkspaceGenerationReference => _providerGenerationObservation?.ModelReference ?? string.Empty;

    public string WorkspaceGenerationModel => string.IsNullOrWhiteSpace(WorkspaceGenerationReference)
        ? "Model unavailable"
        : WorkspaceGenerationReference.Replace('\\', '/').Split('/')[^1];

    public string WorkspaceGenerationAuthor
    {
        get
        {
            string reference = WorkspaceGenerationReference;
            int separator = reference.IndexOf('/');
            return separator > 0 && !reference.StartsWith('.') && !reference.Contains('\\')
                && !reference[..separator].Contains(':') && reference.IndexOf('/', separator + 1) < 0
                    ? reference[..separator]
                    : string.Empty;
        }
    }

    public bool HasWorkspaceGenerationAuthor => WorkspaceGenerationAuthor.Length > 0;

    public string? WorkspaceGenerationVersion => _providerGenerationObservation?.RuntimeVersion;

    public bool HasDifferentWorkspaceGenerationVersion => !string.IsNullOrWhiteSpace(WorkspaceGenerationVersion)
        && !string.Equals(WorkspaceGenerationVersion, WorkspaceVersion, StringComparison.Ordinal);

    public string WorkspaceGenerationHelp => _providerGenerationObservation is not { } observation
        ? "Metrics for the last generation in this application session. Prompt and message content are not written to the observation log."
        : $"Last generation: {observation.ProviderName}; runtime {observation.RuntimeVersion ?? "unavailable"}; completed {observation.CompletedAtUtc.ToLocalTime():g}. "
            + "Input is prompt evaluation; Output is generation. Durations and rates in the table come from the provider. "
            + "Total duration and first output are measured by Alicia and may include overhead. Cached input is part of input, not additional tokens. "
            + "An em dash means unavailable. Prompt and message content are not written to the observation log.";

    public string WorkspaceInputTokens => FormatWorkspaceInteger(_providerGenerationObservation?.InputTokens);

    public string WorkspaceOutputTokens => FormatWorkspaceInteger(_providerGenerationObservation?.OutputTokens);

    public string WorkspaceCachedTokens => FormatWorkspaceCount(_providerGenerationObservation?.CachedInputTokens);

    public string WorkspaceInputDuration => FormatWorkspaceDuration(_providerGenerationObservation?.PromptEvaluationDuration);

    public string WorkspaceOutputDuration => FormatWorkspaceDuration(_providerGenerationObservation?.GenerationDuration);

    public string WorkspaceTotalDuration => FormatWorkspaceDuration(_providerGenerationObservation?.Duration);

    public string WorkspaceFirstOutput => FormatWorkspaceDuration(_providerGenerationObservation?.TimeToFirstOutput);

    public string WorkspaceInputRate => FormatWorkspaceRate(_providerGenerationObservation?.PromptTokensPerSecond);

    public string WorkspaceOutputRate => FormatWorkspaceRate(_providerGenerationObservation?.GenerationTokensPerSecond);

    public string WorkspaceRuntimeSize => FormatWorkspaceStorage(_providerStorageInfo?.RuntimeBytes).Value;

    public string WorkspaceRuntimeUnit => FormatWorkspaceStorage(_providerStorageInfo?.RuntimeBytes).Unit;

    public string WorkspaceCacheSize => FormatWorkspaceStorage(_providerStorageInfo?.ModelCacheBytes).Value;

    public string WorkspaceCacheUnit => FormatWorkspaceStorage(_providerStorageInfo?.ModelCacheBytes).Unit;

    private static string FormatWorkspaceInteger(int? value) => value?.ToString("F0", CultureInfo.CurrentCulture) ?? "—";

    private static string FormatWorkspaceCount(int? value) => value?.ToString("F3", CultureInfo.CurrentCulture) ?? "—";

    private static string FormatWorkspaceDuration(TimeSpan? value) => value?.TotalSeconds.ToString("F3", CultureInfo.CurrentCulture) ?? "—";

    private static string FormatWorkspaceRate(double? value) => value?.ToString("F3", CultureInfo.CurrentCulture) ?? "—";

    private static (string Value, string Unit) FormatWorkspaceStorage(long? bytes)
    {
        if (bytes is not long value)
        {
            return ("—", string.Empty);
        }

        (double divisor, string unit) = value switch
        {
            >= 1024L * 1024L * 1024L => (1024d * 1024d * 1024d, "GiB"),
            >= 1024L * 1024L => (1024d * 1024d, "MiB"),
            >= 1024L => (1024d, "KiB"),
            _ => (1d, "B"),
        };
        return ((value / divisor).ToString("F3", CultureInfo.CurrentCulture), unit);
    }

    private void RaiseWorkspaceMetricsChanged()
    {
        OnPropertyChanged(nameof(WorkspaceGenerationOutcome));
        OnPropertyChanged(nameof(WorkspaceGenerationCompleted));
        OnPropertyChanged(nameof(WorkspaceGenerationCancelled));
        OnPropertyChanged(nameof(WorkspaceGenerationFailed));
        OnPropertyChanged(nameof(WorkspaceGenerationFailure));
        OnPropertyChanged(nameof(WorkspaceGenerationReference));
        OnPropertyChanged(nameof(WorkspaceGenerationModel));
        OnPropertyChanged(nameof(WorkspaceGenerationAuthor));
        OnPropertyChanged(nameof(HasWorkspaceGenerationAuthor));
        OnPropertyChanged(nameof(WorkspaceGenerationVersion));
        OnPropertyChanged(nameof(HasDifferentWorkspaceGenerationVersion));
        OnPropertyChanged(nameof(WorkspaceGenerationHelp));
        OnPropertyChanged(nameof(WorkspaceInputTokens));
        OnPropertyChanged(nameof(WorkspaceOutputTokens));
        OnPropertyChanged(nameof(WorkspaceCachedTokens));
        OnPropertyChanged(nameof(WorkspaceInputDuration));
        OnPropertyChanged(nameof(WorkspaceOutputDuration));
        OnPropertyChanged(nameof(WorkspaceTotalDuration));
        OnPropertyChanged(nameof(WorkspaceFirstOutput));
        OnPropertyChanged(nameof(WorkspaceInputRate));
        OnPropertyChanged(nameof(WorkspaceOutputRate));
        OnPropertyChanged(nameof(WorkspaceRuntimeSize));
        OnPropertyChanged(nameof(WorkspaceRuntimeUnit));
        OnPropertyChanged(nameof(WorkspaceCacheSize));
        OnPropertyChanged(nameof(WorkspaceCacheUnit));
    }
}
