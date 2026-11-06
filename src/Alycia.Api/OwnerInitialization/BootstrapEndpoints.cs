using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Alycia.Modules.Identity.Application;
using Microsoft.AspNetCore.Http.Features;

namespace Alycia.Api.OwnerInitialization;

public static class BootstrapEndpoints
{
    public static bool IsLocalConnection(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote);

    public static void MapBootstrapEndpoints(this WebApplication app)
    {
        var endpoints = app.MapGroup("/api/identity/bootstrap").WithTags("Identity bootstrap");
        endpoints.MapGet("/status", StatusAsync)
            .WithSummary("Check whether the sole owner has been initialized (local only)");
        endpoints.MapPost("", CreateAsync)
            .WithSummary("Initialize the sole owner once (local only)")
            .Accepts<BootstrapRequest>("application/json");
    }

    public static IResult Denied(HttpContext context) => Reply(context,
        StatusCodes.Status403Forbidden, "LOCAL_ONLY", "Local bootstrap access only.");

    private static async Task<IResult> StatusAsync(
        HttpContext context, BootstrapOwnerService service, ILoggerFactory loggers)
    {
        try
        {
            var initialized = await service.IsInitializedAsync(context.RequestAborted);
            return Reply(context, StatusCodes.Status200OK, "BOOTSTRAP_STATUS", "Owner status retrieved.",
                new { initialized });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(context, exception, loggers);
        }
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context, BootstrapOwnerService service, ILoggerFactory loggers)
    {
        if (!context.Request.HasJsonContentType())
            return Reply(context, StatusCodes.Status415UnsupportedMediaType,
                "UNSUPPORTED_MEDIA_TYPE", "A JSON request body is required.");

        BootstrapRequest? request;
        try
        {
            // Constrain attacker-controlled material before deserializing strings.
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = 8192;
            }
            if (context.Request.ContentLength is > 8192)
            {
                return Reply(context, StatusCodes.Status413PayloadTooLarge,
                    "REQUEST_TOO_LARGE", "Request body exceeds the local limit.");
            }
            request = await context.Request.ReadFromJsonAsync<BootstrapRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            return Reply(context, StatusCodes.Status400BadRequest,
                "INVALID_JSON", "Invalid JSON request.");
        }
        catch (BadHttpRequestException exception)
        {
            return Reply(context, exception.StatusCode, "INVALID_REQUEST", "Invalid request body.");
        }
        if (request is null)
            return Reply(context, StatusCodes.Status400BadRequest,
                "INVALID_REQUEST", "A request body is required.");

        try
        {
            var result = await service.InitializeAsync(request.Secret, request.LoginIdentifier,
                request.Password, request.PasswordConfirmation, context.RequestAborted);
            return result.Code switch
            {
                BootstrapResultCode.Created => Reply(context, StatusCodes.Status201Created,
                    "OWNER_INITIALIZED", "Owner initialized.", new { ownerId = result.OwnerId }),
                BootstrapResultCode.AlreadyInitialized => Reply(context, StatusCodes.Status409Conflict,
                    "OWNER_ALREADY_INITIALIZED", "Initialization is no longer available."),
                BootstrapResultCode.NotIssued => Reply(context, StatusCodes.Status409Conflict,
                    "BOOTSTRAP_NOT_READY", "An administrative bootstrap secret is required."),
                BootstrapResultCode.Expired => Reply(context, StatusCodes.Status410Gone,
                    "BOOTSTRAP_EXPIRED", "The bootstrap secret is no longer valid."),
                BootstrapResultCode.Locked => Reply(context, StatusCodes.Status429TooManyRequests,
                    "BOOTSTRAP_LOCKED", "Bootstrap is locked; local administrative regeneration is required."),
                BootstrapResultCode.InvalidSecret => Reply(context, StatusCodes.Status401Unauthorized,
                    "INVALID_BOOTSTRAP_SECRET", "Invalid bootstrap secret."),
                BootstrapResultCode.InvalidLogin => Reply(context, StatusCodes.Status400BadRequest,
                    "INVALID_LOGIN_IDENTIFIER", "Invalid login identifier."),
                BootstrapResultCode.InvalidPassword => Reply(context, StatusCodes.Status400BadRequest,
                    "INVALID_PASSWORD", "Password is invalid or locally blocked."),
                BootstrapResultCode.PasswordConfirmationMismatch => Reply(context, StatusCodes.Status400BadRequest,
                    "PASSWORD_CONFIRMATION_MISMATCH", "Password confirmation does not match."),
                _ => Reply(context, StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR", "Initialization failed.")
            };
        }
        catch (OwnerAlreadyInitializedException)
        {
            return Reply(context, StatusCodes.Status409Conflict,
                "OWNER_ALREADY_INITIALIZED", "Initialization is no longer available.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(context, exception, loggers);
        }
    }

    private static IResult Failure(HttpContext context, Exception exception, ILoggerFactory loggers)
    {
        // Never log request bodies, secrets, password material or DB error text.
        loggers.CreateLogger("Identity.Bootstrap").LogError(
            "Bootstrap operation failed: {ErrorType}", exception.GetType().Name);
        var unavailable = IsUnavailable(exception);
        return Reply(context, unavailable ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status500InternalServerError,
            unavailable ? "DEPENDENCY_UNAVAILABLE" : "INTERNAL_ERROR",
            unavailable ? "Bootstrap storage is unavailable." : "Initialization failed.");
    }

    private static bool IsUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException) return true;
            if (current is DbException db &&
                (string.IsNullOrEmpty(db.SqlState) || db.SqlState.StartsWith("08", StringComparison.Ordinal)
                 || db.SqlState is "53300" or "57P01" or "57P02" or "57P03")) return true;
        }
        return false;
    }

    private static IResult Reply(HttpContext context, int status, string code, string message, object? data = null) =>
        Results.Json(new BootstrapEnvelope(
            status >= 200 && status < 300, status, code, message, data, [],
            Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            DateTimeOffset.UtcNow), statusCode: status);

    private sealed record BootstrapRequest(string? Secret, string? LoginIdentifier,
        string? Password, string? PasswordConfirmation);
    private sealed record BootstrapEnvelope(bool Success, int Status, string Code,
        string Message, object? Data, string[] Errors, string TraceId, DateTimeOffset Timestamp);
}
