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
    public static void AddLocalChat(this WebApplicationBuilder builder)
    {
        var connection = builder.Configuration["ALYCIA_CHAT_CONNECTION"];
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("ALYCIA_CHAT_CONNECTION is required for the local chat prototype.");
        }

        var ollamaUrl = builder.Configuration["ALYCIA_OLLAMA_BASE_URL"] ?? "http://127.0.0.1:11434/";
        var access = new LocalChatAccess(builder.Configuration);
        var uri = access.ValidateOllamaUrl(ollamaUrl);
        builder.Services.AddSingleton(access);

        var options = new ChatOptions();
        builder.Configuration.GetSection("Chat").Bind(options);
        options.Validate();
        builder.Services.AddSingleton(options);
        builder.Services.AddDbContext<ChatDbContext>(db => db.UseNpgsql(connection,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat")));
        builder.Services.AddScoped<IChatRepository, EfChatRepository>();
        builder.Services.AddScoped<ChatService>();
        builder.Services.AddSingleton(new FileStorage(builder.Configuration["ALYCIA_FILES_DIRECTORY"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "data", "files")));
        builder.Services.AddScoped<FileImport>();
        builder.Services.AddScoped<DocumentAnalysisService>();
        builder.Services.AddScoped<ProjectSourceReader>();
        builder.Services.AddScoped<IProjectIndex, ProjectIndexService>();
        builder.Services.AddSingleton<LexicalIndexCache>();
        builder.Services.AddScoped<IProjectRetrieval, ProjectRetrievalService>();
        builder.Services.AddHttpClient<ILocalEmbeddings, OllamaEmbeddings>(http =>
        { http.BaseAddress = uri; http.Timeout = Timeout.InfiniteTimeSpan; http.MaxResponseContentBufferSize = 8 * 1024 * 1024; })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        builder.Services.AddScoped<ProjectToolService>();
        builder.Services.AddHttpClient<IProjectTools, ProjectMcpClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = FileImport.MaxBatchBytes);
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = FileImport.MaxBatchBytes + 1024 * 1024);
        builder.Services.AddHttpClient<IChatModel, OllamaChatModel>(http =>
        {
            http.BaseAddress = uri;
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }

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
                app.Logger.LogError("Chat request failed: {ErrorType}", exception.GetType().Name);
                return exception is DbException or DbUpdateException or TimeoutException
                    ? Error(http, 503, "STORAGE_UNAVAILABLE", "PostgreSQL est indisponible ou le schéma manque. Vérifiez la connexion et lancez make migrate.")
                    : Error(http, 500, "INTERNAL_ERROR", "L’opération a échoué. Consultez les journaux du backend.");
            }
        });

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
            Results.Ok(await repository.ListAsync(ct)));

        routes.MapPost("/conversations", async (CreateConversation request, ChatService service, CancellationToken ct) =>
        {
            var conversation = await service.CreateAsync(request.Title, request.Model, request.SystemPrompt, ct);
            return Results.Created($"/api/chat/conversations/{conversation.Id}", conversation);
        });

        routes.MapGet("/conversations/{id:guid}/messages", async (Guid id, int? before,
            IChatRepository repository, ChatDbContext database, CancellationToken ct) =>
        {
            if (before is <= 0)
            {
                throw new ArgumentException("Le curseur d’historique doit être positif.");
            }

            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            var messages = await repository.MessagesAsync(id, 100, before, ct);
            var minimum = messages.Count == 0 ? 1 : messages[0].Sequence;
            var files = await database.Attachments.AsNoTracking().Where(f => f.ConversationId == id
                && f.AnchorSequence >= minimum && (before == null || f.AnchorSequence < before))
                .Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status, f.AnchorSequence,
                    f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource)).ToListAsync(ct);
            return Results.Ok(new { conversation, messages, files, hasMore = messages.Count > 0 && messages[0].Sequence > 1 });
        });

        routes.MapPatch("/conversations/{id:guid}", async (Guid id, RenameConversation request,
            IChatRepository repository, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            conversation.Rename(request.Title);
            await repository.SaveAsync(ct);
            return Results.Ok(conversation);
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

        routes.MapPost("/conversations/{id:guid}/files/{batchId:guid}", async (Guid id, Guid batchId,
            HttpRequest request, FileImport importer, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Error(request.HttpContext, 415, "INVALID_UPLOAD", "Un formulaire multipart est requis.");
            IFormCollection form;
            try { form = await request.ReadFormAsync(ct); }
            catch (InvalidDataException) { return Error(request.HttpContext, 413, "UPLOAD_TOO_LARGE", "Import limité à 100 Mio."); }
            SourceFileMetadata[] metadata;
            try
            {
                metadata = form.TryGetValue("metadata", out var raw)
                ? JsonSerializer.Deserialize<SourceFileMetadata[]>(raw.ToString(), StreamJson) ?? [] : [];
            }
            catch (JsonException) { throw new ArgumentException("Métadonnées de fichiers invalides."); }
            if (metadata.Length > FileImport.MaxEntries || metadata.Any(m => m is null || string.IsNullOrEmpty(m.Name))
                || metadata.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() != metadata.Length)
                throw new ArgumentException("Métadonnées de fichiers invalides.");
            var lookup = metadata.ToDictionary(m => m.Name, StringComparer.Ordinal);
            var incoming = form.Files.Select(file =>
            {
                lookup.TryGetValue(file.FileName, out var source);
                return new IncomingFile(file.FileName, file.OpenReadStream(), source?.CreatedAt?.ToUniversalTime().ToString("O") ?? "",
                    source?.ModifiedAt?.ToUniversalTime().ToString("O") ?? "", source is null ? "unknown" : "Dates rapportées par le système client");
            }).ToArray();
            try { return Results.Ok(await importer.ImportAsync(id, batchId, incoming, ct)); }
            finally { foreach (var file in incoming) await file.Content.DisposeAsync(); }
        });

        routes.MapGet("/conversations/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId,
            ChatDbContext database, FileStorage storage, HttpContext http, CancellationToken ct) =>
        {
            var file = await database.Attachments.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId && f.ConversationId == id, ct);
            if (file is null) return Error(http, 404, "FILE_NOT_FOUND", "Fichier introuvable.");
            var path = storage.PathFor(id, fileId);
            if (!File.Exists(path)) return Error(http, 503, "FILE_UNAVAILABLE", "Le volume de fichiers est indisponible.");
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(path, "application/octet-stream", Path.GetFileName(file.Name), enableRangeProcessing: true);
        });

        routes.MapDelete("/conversations/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId,
            ChatDbContext database, FileStorage storage, HttpContext http, CancellationToken ct) =>
        {
            var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new ConversationNotFoundException();
            var all = await database.Attachments.Where(f => f.ConversationId == id).ToListAsync(ct);
            if (!all.Any(f => f.Id == fileId)) return Error(http, 404, "FILE_NOT_FOUND", "Fichier introuvable.");
            var ids = new HashSet<Guid> { fileId };
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var file in all)
                    if (file.ArchiveId.HasValue && ids.Contains(file.ArchiveId.Value)) changed |= ids.Add(file.Id);
            }
            var files = all.Where(f => ids.Contains(f.Id)).ToArray();
            database.Attachments.RemoveRange(files);
            conversation.AttachFiles();
            await new EfChatRepository(database).SaveAsync(ct);
            foreach (var file in files)
            {
                try { File.Delete(storage.PathFor(id, file.Id)); }
                catch (IOException) { app.Logger.LogWarning("File cleanup pending: {Id}", file.Id); }
                catch (UnauthorizedAccessException) { app.Logger.LogWarning("File cleanup denied: {Id}", file.Id); }
            }
            return Results.NoContent();
        });

        routes.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessage request,
            ChatService service, CancellationToken ct) =>
            Results.Ok(await service.SendAsync(id, request.RequestId, request.Content, ct)));
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

    private sealed record CreateConversation(string? Title, string? Model, string? SystemPrompt);
    private sealed record RenameConversation(string? Title);
    private sealed record SendMessage(Guid RequestId, string? Content);
}
