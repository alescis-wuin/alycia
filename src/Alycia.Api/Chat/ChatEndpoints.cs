using System.Data.Common;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    public static void MapLocalChat(this WebApplication app)
    {
        var access = app.Services.GetRequiredService<LocalChatAccess>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/chat")
                && !access.IsAllowed(context))
            {
                await Error(context, 403, "LOCAL_ONLY", "Le chat est réservé au client local.").ExecuteAsync(context);
                return;
            }

            await next(context);
        });

        var routes = app.MapGroup("/api/chat").WithTags("Local chat prototype");
        routes.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            try
            {
                return await next(context);
            }
            catch (ArgumentException exception)
            {
                return Error(http, 400, "INVALID_REQUEST", exception.Message);
            }
            catch (ConversationNotFoundException exception)
            {
                return Error(http, 404, "CONVERSATION_NOT_FOUND", exception.Message);
            }
            catch (ChatConflictException exception)
            {
                return Error(http, 409, "CONVERSATION_CHANGED", exception.Message);
            }
            catch (ChatModelException exception)
            {
                return Error(http, 503, "OLLAMA_UNAVAILABLE", exception.Message);
            }
            catch (BadHttpRequestException exception)
            {
                return Error(http, exception.StatusCode, "INVALID_UPLOAD", "Requête invalide ou trop volumineuse.");
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (Exception exception)
            {
                // Never log prompts, generated text, connection strings or provider messages.
                app.Logger.LogError("Chat request failed: {ErrorType}", Alycia.Contracts.Diagnostics.FailureType.Name(exception));
                return exception is DbException or DbUpdateException or TimeoutException
                    ? Error(http, 503, "STORAGE_UNAVAILABLE", "PostgreSQL est indisponible ou le schéma manque. Vérifiez la connexion et lancez make migrate.")
                    : Error(http, 500, "INTERNAL_ERROR", "L’opération a échoué. Consultez les journaux du backend.");
            }
        });

        MapProjects(routes);
        MapConfiguration(routes);
        MapMemory(routes);
        MapAnalysis(routes);
        MapStreaming(routes);
        MapHistory(routes);
        MapMcp(routes);
        MapIndex(routes);

        routes.MapGet("/models", async (IChatModel model, CancellationToken ct) =>
            Results.Ok(await model.ListModelsAsync(ct)));

        routes.MapGet("/conversations", async (IChatRepository repository, CancellationToken ct) =>
            Results.Ok((await repository.ListAsync(ct)).Select(c => c.ToResponse())));

        routes.MapPost("/conversations", async (CreateConversation request, ConversationCreation creation, CancellationToken ct) =>
        {
            var conversation = await creation.CreateAsync(request.Title, request.Model, request.SystemPrompt, request.WorkspaceId, ct);
            return Results.Created($"/api/chat/conversations/{conversation.Id}", conversation.ToResponse());
        });

        routes.MapGet("/conversations/{id:guid}/messages", async (Guid id, int? before,
            IChatRepository repository, ConversationFiles documents, CancellationToken ct) =>
        {
            if (before is <= 0)
            {
                throw new ArgumentException("Le curseur d’historique doit être positif.");
            }

            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            var messages = await repository.MessagesAsync(id, 100, before, ct);
            var minimum = messages.Count == 0 ? 1 : messages[0].Sequence;
            var files = await documents.ForHistoryAsync(id, minimum, before, ct);
            return Results.Ok(new { conversation = conversation.ToResponse(), messages = messages.Select(m => m.ToResponse()), files, hasMore = messages.Count > 0 && messages[0].Sequence > 1 });
        });

        routes.MapPatch("/conversations/{id:guid}", async (Guid id, RenameConversation request,
            IChatRepository repository, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            conversation.Rename(request.Title);
            await repository.SaveAsync(ct);
            return Results.Ok(conversation.ToResponse());
        });

        routes.MapDelete("/conversations/{id:guid}", async (Guid id, IChatRepository repository, FileStorage storage, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            await repository.DeleteAsync(conversation, ct);
            try { storage.DeleteConversation(id); }
            catch (IOException) { app.Logger.LogWarning("File cleanup pending for deleted conversation {Id}", id); }
            catch (UnauthorizedAccessException) { app.Logger.LogWarning("File cleanup denied for deleted conversation {Id}", id); }
            return Results.NoContent();
        });

        routes.MapPost("/conversations/{id:guid}/files/{batchId:guid}", (Guid id, Guid batchId,
            HttpRequest request, FileImport importer, CancellationToken ct) => UploadFilesAsync(id, batchId, request, importer, false, ct));

        routes.MapGet("/conversations/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId,
            IConversationOriginals documents, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var file = await documents.DownloadAsync(id, fileId, ct);
                if (file is null) return Error(http, 404, "FILE_NOT_FOUND", "Fichier introuvable.");
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Results.File(file.Content, "application/octet-stream", file.Name, file.LastModified, enableRangeProcessing: true);
            }
            catch (OriginalUnavailableException error)
            {
                return Error(http, 503, "FILE_UNAVAILABLE", error.Message);
            }
        });
        routes.MapDelete("/conversations/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId,
            ConversationFiles documents, HttpContext http, CancellationToken ct) =>
            await documents.RemoveAsync(id, fileId, ct) ? Results.NoContent() : Error(http, 404, "FILE_NOT_FOUND", "Fichier introuvable."));

        routes.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessage request,
            ChatService service, CancellationToken ct) =>
            Results.Ok((await service.SendAsync(id, request.RequestId, request.Content, ct)).ToResponse()));
    }

    private static async Task<IResult> UploadFilesAsync(Guid id, Guid batchId, HttpRequest request, FileImport importer, bool workspace, CancellationToken ct)
    {
        if (!request.HasFormContentType) return Error(request.HttpContext, 415, "INVALID_UPLOAD", "Un formulaire multipart est requis.");
        var limits = await importer.LimitsAsync(ct);
        var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false }) sizeLimit.MaxRequestBodySize = limits.BatchBytes + 32L * 1024 * 1024;
        request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
        { MultipartBodyLengthLimit = limits.BatchBytes, ValueLengthLimit = 16 * 1024 * 1024 }));
        IFormCollection form;
        try { form = await request.ReadFormAsync(ct); }
        catch (InvalidDataException) { return Error(request.HttpContext, 413, "UPLOAD_TOO_LARGE", $"Import limité à {limits.BatchMiB} Mio."); }
        SourceFileMetadata[] metadata;
        try
        {
            metadata = form.TryGetValue("metadata", out var raw)
            ? JsonSerializer.Deserialize<SourceFileMetadata[]>(raw.ToString(), StreamJson) ?? [] : [];
        }
        catch (JsonException) { throw new ArgumentException("Métadonnées de fichiers invalides."); }
        if (metadata.Length > limits.BatchEntries || metadata.Any(m => m is null || string.IsNullOrEmpty(m.Name))
            || metadata.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() != metadata.Length)
            throw new ArgumentException("Métadonnées de fichiers invalides.");
        var lookup = metadata.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var incoming = form.Files.Select(file =>
        {
            lookup.TryGetValue(file.FileName, out var source);
            return new IncomingFile(file.FileName, file.OpenReadStream(), source?.CreatedAt?.ToUniversalTime().ToString("O") ?? "",
                source?.ModifiedAt?.ToUniversalTime().ToString("O") ?? "", source is null ? "unknown" : "Dates rapportées par le système client");
        }).ToArray();
        try { return Results.Ok(await importer.ImportAsync(id, batchId, incoming, ct, workspace)); }
        finally { foreach (var file in incoming) await file.Content.DisposeAsync(); }
    }

    private static IResult Error(HttpContext context, int status, string code, string message) =>
        Results.Json(new
        {
            success = false,
            status,
            code,
            message,
            data = (object?)null,
            errors = Array.Empty<string>(),
            traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        }, statusCode: status);

    private sealed record CreateConversation(string? Title, string? Model, string? SystemPrompt, Guid? WorkspaceId = null);
    private sealed record RenameConversation(string? Title);
    private sealed record SendMessage(Guid RequestId, string? Content);
}
