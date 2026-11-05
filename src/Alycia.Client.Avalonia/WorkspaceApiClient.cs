using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alycia.Client.Avalonia;

public enum WorkspaceOperationState
{
    Success,
    Invalid,
    Conflict,
    NotFound,
    Disabled,
    Denied,
    Unavailable,
    ConfigurationError,
    Error
}

public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAtUtc);

public sealed record WorkspaceOperationResult(
    WorkspaceOperationState State,
    string Message,
    WorkspaceSummary? Workspace = null);

public sealed class WorkspaceApiClient
{
    private const string DefaultBaseUrl = "http://127.0.0.1:8080";
    private static readonly HttpClient SharedClient = new();
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string? _configuredBaseUrl;
    private readonly TimeSpan _timeout;

    public WorkspaceApiClient()
        : this(SharedClient, null, TimeSpan.FromSeconds(30))
    {
    }

    public WorkspaceApiClient(
        HttpClient httpClient,
        string? configuredBaseUrl,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        _httpClient = httpClient;
        _configuredBaseUrl = configuredBaseUrl;
        _timeout = timeout;
    }

    public Task<WorkspaceOperationResult> CreateAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            return Task.FromResult(new WorkspaceOperationResult(
                WorkspaceOperationState.Invalid,
                "Workspace name must contain 1 to 100 characters."));
        }

        return SendAsync(HttpMethod.Post, "/api/workspaces", normalized,
            cancellationToken);
    }

    public Task<WorkspaceOperationResult> GetAsync(
        string? id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty)
        {
            return Task.FromResult(new WorkspaceOperationResult(
                WorkspaceOperationState.Invalid,
                "Enter a valid nonempty Workspace UUID."));
        }

        return SendAsync(HttpMethod.Get, $"/api/workspaces/{parsed:D}", null,
            cancellationToken);
    }

    private async Task<WorkspaceOperationResult> SendAsync(
        HttpMethod method,
        string path,
        string? name,
        CancellationToken cancellationToken)
    {
        var configuredUrl = _configuredBaseUrl
            ?? Environment.GetEnvironmentVariable("ALYCIA_API_BASE_URL")
            ?? DefaultBaseUrl;

        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp
                && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return new(WorkspaceOperationState.ConfigurationError,
                "Invalid API base URL.");
        }

        if (baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback)
        {
            return new(WorkspaceOperationState.ConfigurationError,
                "HTTPS is required for non-local connections.");
        }

        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new CreateWorkspaceRequest(name));
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            var responseText = await response.Content.ReadAsStringAsync(
                timeoutSource.Token);

            WorkspaceEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<WorkspaceEnvelope>(
                    responseText, JsonOptions);
            }
            catch (JsonException)
            {
                envelope = null;
            }

            return InterpretResponse(method, response.StatusCode, envelope);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(WorkspaceOperationState.Unavailable,
                "Workspace request timed out.");
        }
        catch (HttpRequestException)
        {
            return new(WorkspaceOperationState.Unavailable,
                "Cannot connect to the Workspace API.");
        }
    }

    private static WorkspaceOperationResult InterpretResponse(
        HttpMethod method,
        HttpStatusCode statusCode,
        WorkspaceEnvelope? envelope)
    {
        var status = (int)statusCode;
        var expectedStatus = method == HttpMethod.Post
            ? HttpStatusCode.Created
            : HttpStatusCode.OK;

        if (statusCode == expectedStatus)
        {
            if (envelope is { Success: true }
                && envelope.Status == status
                && envelope.Data is { } workspace
                && workspace.Id != Guid.Empty
                && !string.IsNullOrWhiteSpace(workspace.Name)
                && workspace.CreatedAtUtc != default
                && workspace.CreatedAtUtc.Offset == TimeSpan.Zero)
            {
                return new(WorkspaceOperationState.Success,
                    method == HttpMethod.Post
                        ? "Workspace created successfully."
                        : "Workspace retrieved successfully.",
                    workspace);
            }

            return new(WorkspaceOperationState.Error,
                "Unexpected Workspace API response.");
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            return envelope?.Code == "WORKSPACE_NOT_FOUND"
                ? new(WorkspaceOperationState.NotFound, "Workspace not found.")
                : new(WorkspaceOperationState.Disabled,
                    "Workspace API is disabled or unavailable at this address.");
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType =>
                new(WorkspaceOperationState.Invalid,
                    "Workspace request was rejected as invalid."),
            HttpStatusCode.Conflict =>
                new(WorkspaceOperationState.Conflict,
                    "A Workspace with this name already exists."),
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
                new(WorkspaceOperationState.Denied,
                    "Workspace access is denied. Local Development opt-in is required."),
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout =>
                new(WorkspaceOperationState.Unavailable,
                    "Workspace storage or API is unavailable."),
            _ => new(WorkspaceOperationState.Error,
                $"Workspace request failed (HTTP {status}).")
        };
    }

    private sealed record CreateWorkspaceRequest(string? Name);

    private sealed record WorkspaceEnvelope(
        bool Success,
        int Status,
        string? Code,
        string? Message,
        WorkspaceSummary? Data,
        string[]? Errors,
        string? TraceId,
        DateTimeOffset Timestamp);
}
