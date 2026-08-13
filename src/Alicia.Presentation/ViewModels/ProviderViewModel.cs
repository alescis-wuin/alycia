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

    public string ProviderStatusText => _providerSnapshot?.State switch
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
    };

    public string ProviderDetailText => _providerSnapshot?.Detail
        ?? (SelectedProvider is null
            ? "Select an inference provider."
            : "Use Detect to inspect the selected inference provider runtime.");

    public string ProviderVersionText => string.IsNullOrWhiteSpace(_providerSnapshot?.Version)
        ? "Version not detected"
        : $"Version {_providerSnapshot.Version}";

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

    internal void ApplySnapshot(InferenceProviderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _providerSnapshot = snapshot;
        RaiseProjectionChanged();
    }

    internal void SetTransientState(
        InferenceProviderState state,
        string detail,
        string modelReference)
    {
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
        string modelReference)
    {
        _providerSnapshot = new InferenceProviderSnapshot(
            _providerSnapshot?.Name ?? SelectedProvider?.Name ?? "Local AI provider",
            state,
            _providerSnapshot?.Version,
            _providerSnapshot?.IsCudaEnabled ?? false,
            _providerSnapshot?.ExecutablePath,
            string.IsNullOrWhiteSpace(modelReference) ? null : modelReference,
            endpoint: null,
            detail: detail);
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
        OnPropertyChanged(nameof(ProviderVersionText));
        OnPropertyChanged(nameof(IsProviderProgressVisible));
        OnPropertyChanged(nameof(IsProviderProgressIndeterminate));
        OnPropertyChanged(nameof(ProviderProgressValue));
        OnPropertyChanged(nameof(ProviderProgressText));
        OnPropertyChanged(nameof(ProviderProgressDetailText));
    }
}
