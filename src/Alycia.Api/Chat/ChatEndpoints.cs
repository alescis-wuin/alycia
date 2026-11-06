using System.Data.Common;
using System.Diagnostics;
using System.Net;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static class ChatEndpoints
{
    public static void AddLocalChat(this WebApplicationBuilder builder)
    {
        var connection = builder.Configuration["ALYCIA_CHAT_CONNECTION"];
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("ALYCIA_CHAT_CONNECTION is required for the local chat prototype.");
        }

        var ollamaUrl = builder.Configuration["ALYCIA_OLLAMA_BASE_URL"] ?? "http://127.0.0.1:11434/";
        if (!Uri.TryCreate(ollamaUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback
            || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/"
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException("ALYCIA_OLLAMA_BASE_URL must be a loopback HTTP(S) origin.");
        }

        var options = new ChatOptions();
        builder.Configuration.GetSection("Chat").Bind(options);
        options.Validate();
        builder.Services.AddSingleton(options);
        builder.Services.AddDbContext<ChatDbContext>(db => db.UseNpgsql(connection,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat")));
        builder.Services.AddScoped<IChatRepository, EfChatRepository>();
        builder.Services.AddScoped<ChatService>();
        builder.Services.AddHttpClient<IChatModel, OllamaChatModel>(http =>
        {
            http.BaseAddress = uri;
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }

    public static void MapLocalChat(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/chat")
                && (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)
                    || context.Request.Headers.ContainsKey("Origin")))
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
            IChatRepository repository, CancellationToken ct) =>
        {
            if (before is <= 0)
            {
                throw new ArgumentException("Le curseur d’historique doit être positif.");
            }

            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            var messages = await repository.MessagesAsync(id, 100, before, ct);
            return Results.Ok(new { conversation, messages, hasMore = messages.Count > 0 && messages[0].Sequence > 1 });
        });

        routes.MapPatch("/conversations/{id:guid}", async (Guid id, RenameConversation request,
            IChatRepository repository, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            conversation.Rename(request.Title);
            await repository.SaveAsync(ct);
            return Results.Ok(conversation);
        });

        routes.MapDelete("/conversations/{id:guid}", async (Guid id, IChatRepository repository, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            await repository.DeleteAsync(conversation, ct);
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
