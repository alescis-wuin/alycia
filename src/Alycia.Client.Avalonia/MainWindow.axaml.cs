using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class MainWindow : Window
{
    private readonly BackendHealthProbe _probe = new();
    private readonly OwnerBootstrapApiClient _ownerClient = new();

    private readonly TextBlock _statusText;
    private readonly TextBlock _detailsText;
    private readonly Button _retryButton;
    private readonly TextBlock _ownerStatusText;
    private readonly TextBlock _ownerDetailsText;
    private readonly Button _retryOwnerStatusButton;
    private readonly Button _initializeOwnerButton;
    private readonly TextBox _ownerSecretInput;
    private readonly TextBox _ownerLoginInput;
    private readonly TextBox _ownerPasswordInput;
    private readonly TextBox _ownerConfirmationInput;

    private bool _isChecking;
    private bool _isOwnerBusy;
    private bool _ownerSetupAvailable;
    private bool _ownerInitialized;

    public MainWindow()
    {
        InitializeComponent();

        _statusText = Require<TextBlock>("StatusText");
        _detailsText = Require<TextBlock>("DetailsText");
        _retryButton = Require<Button>("RetryButton");
        _ownerStatusText = Require<TextBlock>("OwnerStatusText");
        _ownerDetailsText = Require<TextBlock>("OwnerDetailsText");
        _retryOwnerStatusButton = Require<Button>("RetryOwnerStatusButton");
        _initializeOwnerButton = Require<Button>("InitializeOwnerButton");
        _ownerSecretInput = Require<TextBox>("OwnerSecretInput");
        _ownerLoginInput = Require<TextBox>("OwnerLoginInput");
        _ownerPasswordInput = Require<TextBox>("OwnerPasswordInput");
        _ownerConfirmationInput = Require<TextBox>("OwnerConfirmationInput");

        Opened += async (_, _) =>
        {
            await CheckBackendAsync();
            await CheckOwnerStatusAsync();
        };
        _retryButton.Click += async (_, _) => await CheckBackendAsync();
        _retryOwnerStatusButton.Click += async (_, _) => await CheckOwnerStatusAsync();
        _initializeOwnerButton.Click += async (_, _) => await InitializeOwnerAsync();
    }

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"{name} control not found.");

    private async Task CheckBackendAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        _retryButton.IsEnabled = false;
        _statusText.Text = "Checking...";
        _detailsText.Text = "Checking API connectivity...";

        try
        {
            var result = await _probe.CheckAsync();
            _statusText.Text = result.IsAvailable ? "Available" : "Unavailable";
            _detailsText.Text = result.Message;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Backend check failed: {exception.GetType().Name}");
            _statusText.Text = "Unavailable";
            _detailsText.Text = "An unexpected error occurred while checking the backend.";
        }
        finally
        {
            _isChecking = false;
            _retryButton.IsEnabled = true;
        }
    }

    private async Task CheckOwnerStatusAsync()
    {
        if (_isOwnerBusy) return;
        SetOwnerBusy(true);
        _ownerStatusText.Text = "Loading...";
        _ownerDetailsText.Text = "Checking owner initialization status...";

        try
        {
            var result = await _ownerClient.CheckStatusAsync();
            _ownerInitialized = result.State == OwnerSetupState.Initialized;
            _ownerSetupAvailable = result.State == OwnerSetupState.Ready;
            _ownerStatusText.Text = StatusLabel(result.State);
            _ownerDetailsText.Text = result.Message;
            if (_ownerInitialized) ClearSensitiveInputs();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Owner status check failed: {exception.GetType().Name}");
            _ownerSetupAvailable = false;
            _ownerStatusText.Text = "Error";
            _ownerDetailsText.Text = "An unexpected owner status error occurred.";
        }
        finally
        {
            SetOwnerBusy(false);
        }
    }

    private async Task InitializeOwnerAsync()
    {
        if (_isOwnerBusy || _ownerInitialized || !_ownerSetupAvailable) return;
        SetOwnerBusy(true);
        _ownerStatusText.Text = "Loading...";
        _ownerDetailsText.Text = "Initializing the owner locally...";

        try
        {
            var result = await _ownerClient.InitializeAsync(
                _ownerSecretInput.Text,
                _ownerLoginInput.Text,
                _ownerPasswordInput.Text,
                _ownerConfirmationInput.Text);

            _ownerStatusText.Text = StatusLabel(result.State);
            _ownerDetailsText.Text = result.Message;
            if (result.State is OwnerSetupState.Success or OwnerSetupState.AlreadyInitialized)
            {
                _ownerInitialized = true;
                _ownerSetupAvailable = false;
                ClearSensitiveInputs();
            }
            else if (result.State is OwnerSetupState.Expired or OwnerSetupState.Locked
                or OwnerSetupState.InvalidSecret)
            {
                _ownerSecretInput.Text = "";
            }
            else if (result.State is OwnerSetupState.Unavailable or OwnerSetupState.Denied
                or OwnerSetupState.Disabled or OwnerSetupState.ConfigurationError)
            {
                _ownerSetupAvailable = false;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Owner initialization failed: {exception.GetType().Name}");
            _ownerStatusText.Text = "Error";
            _ownerDetailsText.Text = "An unexpected owner initialization error occurred.";
        }
        finally
        {
            SetOwnerBusy(false);
        }
    }

    private void SetOwnerBusy(bool busy)
    {
        _isOwnerBusy = busy;
        _retryOwnerStatusButton.IsEnabled = !busy;
        _initializeOwnerButton.IsEnabled = !busy && _ownerSetupAvailable && !_ownerInitialized;
        _ownerSecretInput.IsEnabled = !busy && _ownerSetupAvailable && !_ownerInitialized;
        _ownerLoginInput.IsEnabled = !busy && _ownerSetupAvailable && !_ownerInitialized;
        _ownerPasswordInput.IsEnabled = !busy && _ownerSetupAvailable && !_ownerInitialized;
        _ownerConfirmationInput.IsEnabled = !busy && _ownerSetupAvailable && !_ownerInitialized;
    }

    private void ClearSensitiveInputs()
    {
        _ownerSecretInput.Text = "";
        _ownerPasswordInput.Text = "";
        _ownerConfirmationInput.Text = "";
    }

    private static string StatusLabel(OwnerSetupState state) => state switch
    {
        OwnerSetupState.Ready => "Ready",
        OwnerSetupState.Initialized or OwnerSetupState.AlreadyInitialized => "Already initialized",
        OwnerSetupState.Success => "Success",
        OwnerSetupState.InvalidInput or OwnerSetupState.InvalidPassword
            or OwnerSetupState.ConfirmationMismatch => "Invalid input",
        OwnerSetupState.InvalidSecret => "Invalid secret",
        OwnerSetupState.NotReady => "Secret required",
        OwnerSetupState.Expired => "Expired",
        OwnerSetupState.Locked => "Locked",
        OwnerSetupState.Denied => "Access denied",
        OwnerSetupState.Disabled => "API disabled",
        OwnerSetupState.Unavailable => "Unavailable",
        OwnerSetupState.ConfigurationError => "Configuration error",
        _ => "Error"
    };
}
