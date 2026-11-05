using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class MainWindow : Window
{
    private readonly BackendHealthProbe _probe = new();
    private readonly WorkspaceApiClient _workspaceClient = new();

    private readonly TextBlock _statusText;
    private readonly TextBlock _detailsText;
    private readonly Button _retryButton;
    private readonly TextBox _workspaceNameInput;
    private readonly TextBox _workspaceIdInput;
    private readonly Button _createWorkspaceButton;
    private readonly Button _getWorkspaceButton;
    private readonly TextBlock _workspaceStatusText;
    private readonly TextBlock _workspaceDetailsText;

    private bool _isChecking;
    private bool _isWorkspaceBusy;

    public MainWindow()
    {
        InitializeComponent();

        _statusText = Require<TextBlock>("StatusText");
        _detailsText = Require<TextBlock>("DetailsText");
        _retryButton = Require<Button>("RetryButton");
        _workspaceNameInput = Require<TextBox>("WorkspaceNameInput");
        _workspaceIdInput = Require<TextBox>("WorkspaceIdInput");
        _createWorkspaceButton = Require<Button>("CreateWorkspaceButton");
        _getWorkspaceButton = Require<Button>("GetWorkspaceButton");
        _workspaceStatusText = Require<TextBlock>("WorkspaceStatusText");
        _workspaceDetailsText = Require<TextBlock>("WorkspaceDetailsText");

        Opened += async (_, _) => await CheckBackendAsync();
        _retryButton.Click += async (_, _) => await CheckBackendAsync();
        _createWorkspaceButton.Click += async (_, _) =>
            await RunWorkspaceActionAsync(
                () => _workspaceClient.CreateAsync(_workspaceNameInput.Text));
        _getWorkspaceButton.Click += async (_, _) =>
            await RunWorkspaceActionAsync(
                () => _workspaceClient.GetAsync(_workspaceIdInput.Text));
    }

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"{name} control not found.");

    private async Task CheckBackendAsync()
    {
        if (_isChecking)
        {
            return;
        }

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
            Console.Error.WriteLine(
                $"Backend check failed: {exception.GetType().Name}");
            _statusText.Text = "Unavailable";
            _detailsText.Text =
                "An unexpected error occurred while checking the backend.";
        }
        finally
        {
            _isChecking = false;
            _retryButton.IsEnabled = true;
        }
    }

    private async Task RunWorkspaceActionAsync(
        Func<Task<WorkspaceOperationResult>> operation)
    {
        if (_isWorkspaceBusy)
        {
            return;
        }

        _isWorkspaceBusy = true;
        _createWorkspaceButton.IsEnabled = false;
        _getWorkspaceButton.IsEnabled = false;
        _workspaceStatusText.Text = "Loading...";
        _workspaceDetailsText.Text = "Contacting the Workspace API...";

        try
        {
            var result = await operation();
            _workspaceStatusText.Text = result.State switch
            {
                WorkspaceOperationState.Success => "Success",
                WorkspaceOperationState.Invalid => "Invalid input",
                WorkspaceOperationState.Conflict => "Name conflict",
                WorkspaceOperationState.NotFound => "Not found",
                WorkspaceOperationState.Disabled => "API disabled",
                WorkspaceOperationState.Denied => "Access denied",
                WorkspaceOperationState.Unavailable => "Unavailable",
                WorkspaceOperationState.ConfigurationError => "Configuration error",
                _ => "Error"
            };

            if (result.Workspace is { } workspace)
            {
                _workspaceNameInput.Text = workspace.Name;
                _workspaceIdInput.Text = workspace.Id.ToString("D");
                _workspaceDetailsText.Text =
                    $"Name: {workspace.Name}\n" +
                    $"ID: {workspace.Id:D}\n" +
                    $"Created (UTC): {workspace.CreatedAtUtc:u}";
            }
            else
            {
                _workspaceDetailsText.Text = result.Message;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Workspace operation failed: {exception.GetType().Name}");
            _workspaceStatusText.Text = "Error";
            _workspaceDetailsText.Text = "An unexpected Workspace error occurred.";
        }
        finally
        {
            _isWorkspaceBusy = false;
            _createWorkspaceButton.IsEnabled = true;
            _getWorkspaceButton.IsEnabled = true;
        }
    }
}
