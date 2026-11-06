using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alycia.Client.Avalonia;

public enum OwnerSetupState
{
    Ready,
    Initialized,
    Success,
    InvalidInput,
    InvalidPassword,
    ConfirmationMismatch,
    InvalidSecret,
    NotReady,
    Expired,
    Locked,
    AlreadyInitialized,
    Denied,
    Disabled,
    Unavailable,
    ConfigurationError,
    Error
}

public sealed record OwnerSetupResult(OwnerSetupState State, string Message);

// S7 only: local, one-time owner initialization. No credentials or sessions are retained.
public sealed class OwnerBootstrapApiClient
{
    private const string DefaultBaseUrl = "http://127.0.0.1:8080";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false
    });
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string? _configuredBaseUrl;
    private readonly TimeSpan _timeout;

    public OwnerBootstrapApiClient()
        : this(SharedClient, null, TimeSpan.FromSeconds(30))
    {
    }

    public OwnerBootstrapApiClient(
        HttpClient httpClient, string? configuredBaseUrl, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _httpClient = httpClient;
        _configuredBaseUrl = configuredBaseUrl;
        _timeout = timeout;
    }

    public Task<OwnerSetupResult> CheckStatusAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, null, cancellationToken);

    public Task<OwnerSetupResult> InitializeAsync(
        string? secret,
        string? loginIdentifier,
        string? password,
        string? confirmation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Task.FromResult(new OwnerSetupResult(OwnerSetupState.InvalidInput,
                "Enter the administrative bootstrap secret."));
        }

        if (!TryNormalizeLogin(loginIdentifier, out var normalizedLogin))
        {
            return Task.FromResult(new OwnerSetupResult(OwnerSetupState.InvalidInput,
                "Username must contain 3 to 64 ASCII letters, digits, dots, underscores or hyphens."));
        }

        if (!TryNormalizePassword(password, out var normalizedPassword))
        {
            return Task.FromResult(new OwnerSetupResult(OwnerSetupState.InvalidPassword,
                "Password must contain 15 to 128 printable Unicode code points after NFC."));
        }

        if (confirmation is null || !TryNormalizePassword(confirmation, out var normalizedConfirmation)
            || !string.Equals(normalizedPassword, normalizedConfirmation, StringComparison.Ordinal))
        {
            return Task.FromResult(new OwnerSetupResult(OwnerSetupState.ConfirmationMismatch,
                "Password confirmation does not match."));
        }

        // Keep the submitted password unchanged; the API enforces NFC and its
        // documented local blocked-password list. Never persist these values.
        return SendAsync(HttpMethod.Post,
            new BootstrapRequest(secret, normalizedLogin, password!, confirmation),
            cancellationToken);
    }

    private async Task<OwnerSetupResult> SendAsync(
        HttpMethod method, BootstrapRequest? form, CancellationToken cancellationToken)
    {
        var configuredUrl = _configuredBaseUrl
            ?? Environment.GetEnvironmentVariable("ALYCIA_API_BASE_URL")
            ?? DefaultBaseUrl;

        // The bootstrap secret must never be sent to any non-loopback host,
        // including an HTTPS endpoint. Do not accept URL credentials or paths.
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || !baseUri.IsLoopback
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment)
            || baseUri.AbsolutePath != "/")
        {
            return new(OwnerSetupState.ConfigurationError,
                "The bootstrap API address must be a local loopback URL.");
        }

        using var request = new HttpRequestMessage(method,
            new Uri(baseUri, method == HttpMethod.Post
                ? "/api/identity/bootstrap"
                : "/api/identity/bootstrap/status"));
        if (form is not null)
        {
            request.Content = JsonContent.Create(form);
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            var responseText = await response.Content.ReadAsStringAsync(timeoutSource.Token);

            BootstrapEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<BootstrapEnvelope>(responseText, JsonOptions);
            }
            catch (JsonException)
            {
                envelope = null;
            }

            return InterpretResponse(method, response.StatusCode, envelope);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(OwnerSetupState.Unavailable, "Bootstrap API request timed out.");
        }
        catch (HttpRequestException)
        {
            return new(OwnerSetupState.Unavailable, "Cannot connect to the local bootstrap API.");
        }
    }

    private static OwnerSetupResult InterpretResponse(
        HttpMethod method, HttpStatusCode httpStatus, BootstrapEnvelope? envelope)
    {
        var status = (int)httpStatus;
        if (httpStatus == HttpStatusCode.OK && method == HttpMethod.Get)
        {
            if (envelope is { Success: true, Status: 200, Code: "BOOTSTRAP_STATUS" }
                && envelope.Data.ValueKind == JsonValueKind.Object
                && envelope.Data.TryGetProperty("initialized", out var initialized)
                && initialized.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return initialized.GetBoolean()
                    ? new(OwnerSetupState.Initialized, "The owner is already initialized. No login is available in S7.")
                    : new(OwnerSetupState.Ready, "Owner setup is available. Generate a local secret before continuing.");
            }

            return new(OwnerSetupState.Error, "Unexpected bootstrap status response.");
        }

        if (httpStatus == HttpStatusCode.Created && method == HttpMethod.Post)
        {
            if (envelope is { Success: true, Status: 201, Code: "OWNER_INITIALIZED" }
                && envelope.Data.ValueKind == JsonValueKind.Object
                && envelope.Data.TryGetProperty("ownerId", out var ownerId)
                && ownerId.ValueKind == JsonValueKind.String
                && Guid.TryParse(ownerId.GetString(), out var parsed)
                && parsed != Guid.Empty)
            {
                return new(OwnerSetupState.Success,
                    "Owner initialized successfully. Sign-in is not implemented in S7.");
            }

            return new(OwnerSetupState.Error, "Unexpected owner initialization response.");
        }

        return (httpStatus, envelope?.Code) switch
        {
            (HttpStatusCode.BadRequest, "INVALID_LOGIN_IDENTIFIER") =>
                new(OwnerSetupState.InvalidInput, "Username is invalid."),
            (HttpStatusCode.BadRequest, "INVALID_PASSWORD") =>
                new(OwnerSetupState.InvalidPassword,
                    "Password is invalid or appears in the local common/compromised-password list."),
            (HttpStatusCode.BadRequest, "PASSWORD_CONFIRMATION_MISMATCH") =>
                new(OwnerSetupState.ConfirmationMismatch, "Password confirmation does not match."),
            (HttpStatusCode.Unauthorized, "INVALID_BOOTSTRAP_SECRET") =>
                new(OwnerSetupState.InvalidSecret, "Bootstrap secret was rejected."),
            (HttpStatusCode.Conflict, "BOOTSTRAP_NOT_READY") =>
                new(OwnerSetupState.NotReady, "Generate a local administrative bootstrap secret first."),
            (HttpStatusCode.Conflict, "OWNER_ALREADY_INITIALIZED") =>
                new(OwnerSetupState.AlreadyInitialized, "Owner initialization is permanently closed."),
            (HttpStatusCode.Gone, "BOOTSTRAP_EXPIRED") =>
                new(OwnerSetupState.Expired, "Bootstrap secret expired. Administrative regeneration is required."),
            (HttpStatusCode.TooManyRequests, "BOOTSTRAP_LOCKED") =>
                new(OwnerSetupState.Locked, "Bootstrap locked. Administrative regeneration is required."),
            (HttpStatusCode.Forbidden, _) =>
                new(OwnerSetupState.Denied,
                    "Only connections from the API process's loopback interface are allowed."),
            (HttpStatusCode.NotFound, _) =>
                new(OwnerSetupState.Disabled, "Bootstrap API is not enabled at this address."),
            (HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout, _) =>
                new(OwnerSetupState.Unavailable, "Bootstrap API or database is unavailable."),
            (HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge
                or HttpStatusCode.UnsupportedMediaType, _) =>
                new(OwnerSetupState.InvalidInput, "Bootstrap request was rejected as invalid."),
            _ => new(OwnerSetupState.Error, $"Bootstrap request failed (HTTP {status}).")
        };
    }

    private static bool TryNormalizeLogin(string? source, out string normalized)
    {
        normalized = source?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length is < 3 or > 64) return false;
        foreach (var character in normalized)
        {
            if (character is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-'))
                return false;
        }
        return true;
    }

    private static bool TryNormalizePassword(string? source, out string normalized)
    {
        normalized = "";
        if (source is null) return false;
        try
        {
            normalized = source.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var count = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            count++;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.OtherNotAssigned)
                return false;
        }
        return count is >= 15 and <= 128;
    }

    private sealed record BootstrapRequest(string Secret, string LoginIdentifier,
        string Password, string PasswordConfirmation);
    private sealed record BootstrapEnvelope(bool Success, int Status, string? Code,
        string? Message, JsonElement Data, string[]? Errors, string? TraceId,
        DateTimeOffset Timestamp);
}
