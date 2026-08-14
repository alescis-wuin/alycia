using Alicia.Application.Providers;

namespace Alicia.Presentation.ViewModels;

public sealed class ProviderViewModel : ViewModelBase
{
    private readonly IInferenceProviderRegistry _providerRegistry;
    private bool _isProviderBusy;
    private InferenceProviderDescriptor? _selectedProvider;
    private IInferenceProviderRuntime? _selectedProviderRuntime;
    private InferenceProviderProgress? _providerProgress;
    private InferenceProviderSnapshot? _providerSnapshot;
    private InferenceProviderFailureKind? _lastFailureKind;
    private string? _lastFailureMessage;
    private InferenceProviderUpdateInfo? _providerUpdateInfo;
    private string? _providerUpdateStatusMessage;

    public ProviderViewModel(IInferenceProviderRegistry providerRegistry)
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);
        _providerRegistry = providerRegistry;
    }

    public IReadOnlyList<InferenceProviderDescriptor> ProviderOptions => _providerRegistry.Providers;

    public InferenceProviderDescriptor? SelectedProvider => _selectedProvider;

    public bool IsProviderBusy
    {
        get => _isProviderBusy;
        internal set => SetProperty(ref _isProviderBusy, value);
    }

    public bool IsProviderRunning => _providerSnapshot?.State == InferenceProviderState.Running;

    public string ProviderName => _providerSnapshot?.Name
        ?? SelectedProvider?.Name
        ?? "Local AI provider";

    public string ProviderStatusText => _lastFailureKind switch
    {
        InferenceProviderFailureKind.Missing => "Not installed",
        InferenceProviderFailureKind.Unsupported => "CUDA unavailable",
        InferenceProviderFailureKind.Network => "Connection problem",
        InferenceProviderFailureKind.Model => "Model needs attention",
        InferenceProviderFailureKind.Faulted => "Needs attention",
        _ => _providerSnapshot?.State switch
        {
            InferenceProviderState.Detecting => "Detecting",
            InferenceProviderState.Missing => "Not installed",
            InferenceProviderState.Ready => "Ready",
            InferenceProviderState.Installing => "Installing",
            InferenceProviderState.Starting => "Starting",
            InferenceProviderState.Running => "Running",
            InferenceProviderState.Stopping => "Stopping",
            InferenceProviderState.Unsupported => "CUDA unavailable",
            InferenceProviderState.Faulted => "Needs attention",
            _ => SelectedProvider is null ? "Not configured" : "Not detected",
        },
    };

    public string ProviderDetailText => _lastFailureMessage
        ?? _providerSnapshot?.Detail
        ?? (SelectedProvider is null
            ? "Select an inference provider."
            : "Use Detect to inspect the selected inference provider runtime.");

    public bool HasProviderFailure => _lastFailureKind is not null;

    public InferenceProviderFailureKind? ProviderFailureKind => _lastFailureKind;

    public string ProviderFailureMessage => _lastFailureMessage ?? string.Empty;

    public string ProviderVersionText => string.IsNullOrWhiteSpace(_providerSnapshot?.Version)
        ? "Version not detected"
        : $"Version {_providerSnapshot.Version}";

    public bool SupportsProviderUpdates => _selectedProviderRuntime is IInferenceProviderUpdateRuntime;

    public bool IsProviderUpdateAvailable => _providerUpdateInfo?.IsUpdateAvailable == true;

    public string ProviderInstalledReleaseText => _providerUpdateInfo?.InstalledVersion is string installedVersion
        ? $"Managed release {installedVersion}"
        : "Managed release not checked";

    public string ProviderValidatedReleaseText => _selectedProviderRuntime is IInferenceProviderUpdateRuntime updateRuntime
        ? $"Alicia validated {updateRuntime.ValidatedVersion}"
        : "Validated release unavailable";

    public string ProviderLatestReleaseText => _providerUpdateInfo?.LatestVersion is string latestVersion
        ? $"Upstream latest {latestVersion}"
        : "Upstream release not checked";

    public string ProviderUpdateStatusText => _providerUpdateStatusMessage
        ?? _providerUpdateInfo?.Detail
        ?? (SupportsProviderUpdates
            ? "Use Check update to compare the managed runtime with Alicia's validated release and upstream latest."
            : "Managed runtime updates are not available for this provider.");

    public bool IsProviderProgressVisible => _providerProgress is not null;

    public bool IsProviderProgressIndeterminate => IsProviderBusy
        && _providerProgress?.Fraction is null;

    public double ProviderProgressValue => (_providerProgress?.Fraction ?? 0) * 100;

    public string ProviderProgressText => _providerProgress is null
        ? string.Empty
        : _providerProgress.Fraction is double fraction
            ? $"{_providerProgress.Stage} • {fraction:P0}"
            : _providerProgress.Stage;

    public string ProviderProgressDetailText => _providerProgress?.Detail ?? string.Empty;

    internal InferenceProviderSnapshot? Snapshot => _providerSnapshot;

    internal void SelectProvider(InferenceProviderDescriptor? descriptor)
    {
        if (Equals(_selectedProvider, descriptor))
        {
            return;
        }

        _selectedProvider = descriptor;
        _selectedProviderRuntime = descriptor is null
            ? null
            : _providerRegistry.GetRequiredRuntime(descriptor.Id);
        _providerSnapshot = null;
        _providerProgress = null;
        _lastFailureKind = null;
        _lastFailureMessage = null;
        _providerUpdateInfo = null;
        _providerUpdateStatusMessage = null;

        OnPropertyChanged(nameof(SelectedProvider));
        RaiseProjectionChanged();
    }

    internal void PersistSelection(string providerId)
    {
        _providerRegistry.SelectProvider(providerId);
    }

    internal IInferenceProviderRuntime GetSelectedProviderRuntime()
    {
        return _selectedProviderRuntime
            ?? throw new InvalidOperationException(
                "Select an inference provider before performing this operation.");
    }

    internal IInferenceProviderUpdateRuntime GetSelectedProviderUpdateRuntime()
    {
        return _selectedProviderRuntime as IInferenceProviderUpdateRuntime
            ?? throw new InvalidOperationException(
                "The selected inference provider does not expose managed update operations.");
    }

    internal void ApplyUpdateInfo(InferenceProviderUpdateInfo updateInfo)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);
        _providerUpdateInfo = updateInfo;
        _providerUpdateStatusMessage = null;
        RaiseProjectionChanged();
    }

    internal void SetUpdateStatusMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _providerUpdateStatusMessage = message.Trim();
        RaiseProjectionChanged();
    }

    internal void ApplySnapshot(InferenceProviderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _providerSnapshot = snapshot;
        _lastFailureKind = snapshot.FailureKind;
        _lastFailureMessage = snapshot.FailureKind is null
            ? null
            : snapshot.Detail;
        RaiseProjectionChanged();
    }

    internal void SetTransientState(
        InferenceProviderState state,
        string detail,
        string modelReference)
    {
        _lastFailureKind = null;
        _lastFailureMessage = null;
        _providerSnapshot = new InferenceProviderSnapshot(
            _providerSnapshot?.Name ?? SelectedProvider?.Name ?? "Local AI provider",
            state,
            _providerSnapshot?.Version,
            _providerSnapshot?.IsCudaEnabled ?? false,
            _providerSnapshot?.ExecutablePath,
            string.IsNullOrWhiteSpace(modelReference) ? null : modelReference,
            _providerSnapshot?.Endpoint,
            detail);
        RaiseProjectionChanged();
    }

    internal void SetFault(
        string detail,
        InferenceProviderState state,
        string modelReference,
        InferenceProviderFailureKind failureKind)
    {
        _providerSnapshot = new InferenceProviderSnapshot(
            _providerSnapshot?.Name ?? SelectedProvider?.Name ?? "Local AI provider",
            state,
            _providerSnapshot?.Version,
            state == InferenceProviderState.Missing
                ? false
                : _providerSnapshot?.IsCudaEnabled ?? false,
            state == InferenceProviderState.Missing
                ? null
                : _providerSnapshot?.ExecutablePath,
            string.IsNullOrWhiteSpace(modelReference) ? null : modelReference,
            endpoint: null,
            detail: detail,
            failureKind: failureKind);
        _lastFailureKind = failureKind;
        _lastFailureMessage = detail;
        RaiseProjectionChanged();
    }

    internal void SetLastFailure(
        InferenceProviderFailureKind failureKind,
        string userMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        _lastFailureKind = failureKind;
        _lastFailureMessage = userMessage.Trim();
        RaiseProjectionChanged();
    }

    internal void ClearFailure()
    {
        if (_lastFailureKind is null && _lastFailureMessage is null)
        {
            return;
        }

        _lastFailureKind = null;
        _lastFailureMessage = null;
        RaiseProjectionChanged();
    }

    internal void ApplyProgress(InferenceProviderProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _providerProgress = progress;
        RaiseProjectionChanged();
    }

    internal void ClearProgress()
    {
        if (_providerProgress is null)
        {
            return;
        }

        _providerProgress = null;
        RaiseProjectionChanged();
    }

    private void RaiseProjectionChanged()
    {
        OnPropertyChanged(nameof(IsProviderRunning));
        OnPropertyChanged(nameof(ProviderName));
        OnPropertyChanged(nameof(ProviderStatusText));
        OnPropertyChanged(nameof(ProviderDetailText));
        OnPropertyChanged(nameof(HasProviderFailure));
        OnPropertyChanged(nameof(ProviderFailureKind));
        OnPropertyChanged(nameof(ProviderFailureMessage));
        OnPropertyChanged(nameof(ProviderVersionText));
        OnPropertyChanged(nameof(SupportsProviderUpdates));
        OnPropertyChanged(nameof(IsProviderUpdateAvailable));
        OnPropertyChanged(nameof(ProviderInstalledReleaseText));
        OnPropertyChanged(nameof(ProviderValidatedReleaseText));
        OnPropertyChanged(nameof(ProviderLatestReleaseText));
        OnPropertyChanged(nameof(ProviderUpdateStatusText));
        OnPropertyChanged(nameof(IsProviderProgressVisible));
        OnPropertyChanged(nameof(IsProviderProgressIndeterminate));
        OnPropertyChanged(nameof(ProviderProgressValue));
        OnPropertyChanged(nameof(ProviderProgressText));
        OnPropertyChanged(nameof(ProviderProgressDetailText));
    }
}
