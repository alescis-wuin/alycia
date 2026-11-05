using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Alycia.Modules.Workspaces.Application;
using Alycia.Modules.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Workspaces;

internal static class WorkspaceEndpoints
{
    public static bool IsLocalConnection(HttpContext context)
    {
        // Do not use Host, X-Forwarded-For or untrusted proxy headers.
        var address = context.Connection.RemoteIpAddress;
        return address is not null && IPAddress.IsLoopback(address);
    }

    public static IResult AccessDenied(HttpContext context) =>
        Respond(context, StatusCodes.Status403Forbidden,
            "LOCAL_ONLY", "Workspace access is restricted to local development.");

    public static void MapWorkspaceEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/workspaces")
            .WithTags("Workspaces");

        routes.MapPost("", CreateAsync)
            .WithName("CreateWorkspace")
            .WithSummary("Create a Workspace (local development only)")
            .Accepts<CreateWorkspaceRequest>("application/json")
            .Produces<WorkspaceEnvelope>(StatusCodes.Status201Created)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status403Forbidden)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status409Conflict)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status503ServiceUnavailable);

        routes.MapGet("/{id}", GetAsync)
            .WithName("GetWorkspace")
            .WithSummary("Retrieve a Workspace (local development only)")
            .Produces<WorkspaceEnvelope>(StatusCodes.Status200OK)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status403Forbidden)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status404NotFound)
            .Produces<WorkspaceEnvelope>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateWorkspaceService service,
        ILoggerFactory loggerFactory)
    {
        if (!context.Request.HasJsonContentType())
        {
            return Respond(context, StatusCodes.Status415UnsupportedMediaType,
                "UNSUPPORTED_MEDIA_TYPE", "A JSON request body is required.");
        }

        CreateWorkspaceRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<CreateWorkspaceRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            return Respond(context, StatusCodes.Status400BadRequest,
                "INVALID_JSON", "The request body must be a valid JSON object.");
        }
        catch (BadHttpRequestException exception)
        {
            return Respond(context, exception.StatusCode,
                "INVALID_REQUEST", "The request body could not be read.");
        }

        if (request is null)
        {
            return Respond(context, StatusCodes.Status400BadRequest,
                "INVALID_REQUEST", "A Workspace name is required.");
        }

        try
        {
            var workspace = await service.ExecuteAsync(
                request.Name, context.RequestAborted);

            context.Response.Headers.Location = $"/api/workspaces/{workspace.Id:D}";

            return Respond(context, StatusCodes.Status201Created,
                "WORKSPACE_CREATED", "Workspace created successfully.",
                ToDto(workspace));
        }
        catch (ArgumentException)
        {
            return Respond(context, StatusCodes.Status400BadRequest,
                "INVALID_WORKSPACE_NAME",
                "Workspace name must be nonempty and at most 100 characters.");
        }
        catch (WorkspaceNameConflictException)
        {
            return Respond(context, StatusCodes.Status409Conflict,
                "WORKSPACE_NAME_CONFLICT", "A Workspace with this name already exists.");
        }
        catch (Exception exception) when (
            IsDatabaseUnavailable(exception))
        {
            // Log type only; provider exception text may contain sensitive data.
            loggerFactory.CreateLogger("Workspaces.Api").LogError(
                "Workspace creation database failure: {ErrorType}",
                exception.GetType().Name);
            return Respond(context, StatusCodes.Status503ServiceUnavailable,
                "DEPENDENCY_UNAVAILABLE", "Workspace storage is unavailable.");
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("Workspaces.Api").LogError(
                "Workspace creation failure: {ErrorType}",
                exception.GetType().Name);
            return Respond(context, StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR", "Workspace creation failed.");
        }
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        string id,
        GetWorkspaceService service,
        ILoggerFactory loggerFactory)
    {
        if (!Guid.TryParse(id, out var workspaceId) || workspaceId == Guid.Empty)
        {
            return Respond(context, StatusCodes.Status400BadRequest,
                "INVALID_WORKSPACE_ID", "A nonempty Workspace UUID is required.");
        }

        try
        {
            var workspace = await service.ExecuteAsync(
                workspaceId, context.RequestAborted);

            return workspace is null
                ? Respond(context, StatusCodes.Status404NotFound,
                    "WORKSPACE_NOT_FOUND", "Workspace not found.")
                : Respond(context, StatusCodes.Status200OK,
                    "WORKSPACE_RETRIEVED", "Workspace retrieved successfully.",
                    ToDto(workspace));
        }
        catch (Exception exception) when (
            IsDatabaseUnavailable(exception))
        {
            loggerFactory.CreateLogger("Workspaces.Api").LogError(
                "Workspace retrieval database failure: {ErrorType}",
                exception.GetType().Name);
            return Respond(context, StatusCodes.Status503ServiceUnavailable,
                "DEPENDENCY_UNAVAILABLE", "Workspace storage is unavailable.");
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("Workspaces.Api").LogError(
                "Workspace retrieval failure: {ErrorType}",
                exception.GetType().Name);
            return Respond(context, StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR", "Workspace retrieval failed.");
        }
    }

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        // EF Core can wrap a provider failure in InvalidOperationException.
        // A data-integrity error is not a storage outage.
        for (Exception? current = exception; current is not null;
             current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }

            if (current is DbException databaseException)
            {
                var sqlState = databaseException.SqlState;
                if (string.IsNullOrEmpty(sqlState)
                    || sqlState.StartsWith("08", StringComparison.Ordinal)
                    || sqlState is "53300" or "57P01" or "57P02" or "57P03")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static WorkspaceDto ToDto(Workspace workspace) =>
        new(workspace.Id, workspace.Name, workspace.CreatedAtUtc);

    private static IResult Respond(
        HttpContext context,
        int status,
        string code,
        string message,
        object? data = null)
    {
        var traceId = Activity.Current?.TraceId.ToString()
            ?? context.TraceIdentifier;

        return Results.Json(
            new WorkspaceEnvelope(
                Success: status >= 200 && status < 300,
                Status: status,
                Code: code,
                Message: message,
                Data: data,
                Errors: [],
                TraceId: traceId,
                Timestamp: DateTimeOffset.UtcNow),
            statusCode: status);
    }

    private sealed record CreateWorkspaceRequest(string? Name);

    private sealed record WorkspaceDto(
        Guid Id,
        string Name,
        DateTimeOffset CreatedAtUtc);

    private sealed record WorkspaceEnvelope(
        bool Success,
        int Status,
        string Code,
        string Message,
        object? Data,
        string[] Errors,
        string TraceId,
        DateTimeOffset Timestamp);
}
