using Alycia.Modules.Testing;

namespace Alycia.Api.Chat;

internal static class DevelopmentInference
{
    public static void AddMocks(IServiceCollection services)
    {
        services.AddSingleton<ITextGeneration>(_ => new ScriptedTextGeneration());
        services.AddSingleton<ILocalEmbeddings, DeterministicEmbeddings>();
    }

    public static void MapDemo(WebApplication app)
    {
        var group = app.MapGroup("/api/chat");
        group.AddEndpointFilter(async (context, next) => app.Services.GetRequiredService<LocalChatAccess>().IsAllowed(context.HttpContext)
            ? await next(context) : Results.Json(new { code = "LOCAL_ONLY" }, statusCode: 403));
        group.MapGet("/models", (ITextGeneration provider, CancellationToken ct) => provider.ListModelsAsync(ct));
        group.MapGet("/mock/sample", async (ITextGeneration provider, CancellationToken ct) =>
        {
            var chunks = new List<string>();
            var result = await provider.StreamAsync(ScriptedTextGeneration.Model, [new("user", "Bonjour")],
                new(2048, 64, false, 0, 1, 10, "chat", null),
                delta => { chunks.Add(delta.Content); return Task.CompletedTask; }, false, ct);
            return Results.Ok(new { model = ScriptedTextGeneration.Model, chunks, response = result.Content });
        });
    }
}
