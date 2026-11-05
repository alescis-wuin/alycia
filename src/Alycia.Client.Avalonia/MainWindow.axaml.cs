
using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class MainWindow : Window
{
    private readonly BackendHealthProbe _probe = new();

    private readonly TextBlock _statusText;
    private readonly TextBlock _detailsText;
    private readonly Button _retryButton;

    private bool _isChecking;

    public MainWindow()
    {
        InitializeComponent();

        _statusText = this.FindControl<TextBlock>("StatusText")
            ?? throw new InvalidOperationException(
                "StatusText control not found.");

        _detailsText = this.FindControl<TextBlock>("DetailsText")
            ?? throw new InvalidOperationException(
                "DetailsText control not found.");

        _retryButton = this.FindControl<Button>("RetryButton")
            ?? throw new InvalidOperationException(
                "RetryButton control not found.");

        Opened += async (_, _) => await CheckBackendAsync();
        _retryButton.Click += async (_, _) => await CheckBackendAsync();
    }

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

            _statusText.Text = result.IsAvailable
                ? "Available"
                : "Unavailable";

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
}
