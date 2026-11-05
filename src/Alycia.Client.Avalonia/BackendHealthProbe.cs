
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Alycia.Client.Avalonia;

internal sealed record BackendHealthResult(
    bool IsAvailable,
    string Message);

internal sealed class BackendHealthProbe
{
    private const string DefaultBaseUrl = "http://127.0.0.1:8080";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly HttpClient Client = new();

    public async Task<BackendHealthResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var configuredUrl =
            Environment.GetEnvironmentVariable("ALYCIA_API_BASE_URL")
            ?? DefaultBaseUrl;

        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp
                && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return new(false, "Invalid API base URL.");
        }

        if (baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback)
        {
            return new(false, "HTTPS is required for non-local connections.");
        }

        var healthUri = new Uri(baseUri, "/healthz");

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Timeout);

        try
        {
            using var response =
                await Client.GetAsync(healthUri, timeoutSource.Token);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new(
                    false,
                    $"Backend returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(
                timeoutSource.Token);

            return string.Equals(body, "Healthy", StringComparison.Ordinal)
                ? new(true, "Backend available.")
                : new(false, "Unexpected backend health response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "Backend request timed out.");
        }
        catch (HttpRequestException)
        {
            return new(false, "Cannot connect to the backend.");
        }
    }
}
