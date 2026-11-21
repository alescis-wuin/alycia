using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static class ChatRegistration
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
        RegisterDocuments(builder);
        RegisterIndexing(builder.Services);
        RegisterRetrieval(builder.Services);
        builder.Services.AddScoped<TemporalMemoryStore>();
        builder.Services.AddScoped<ITemporalMemory>(sp => sp.GetRequiredService<TemporalMemoryStore>());
        builder.Services.AddScoped<ProjectToolService>();
        builder.Services.AddScoped<ISnapshotProjectTools>(sp => sp.GetRequiredService<ProjectToolService>());
        builder.Services.AddHttpClient<IProjectTools, ProjectMcpClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        RegisterInference(builder.Services, uri);
    }

    private static void RegisterIndexing(IServiceCollection services)
    {
        services.AddScoped<IProjectIndex, ProjectIndexService>();
        services.AddScoped<IndexCatalog>();
        services.AddScoped<KnowledgeStore>();
        services.AddScoped<KnowledgeBuilder>();
        services.AddScoped<PostgresEmbeddingStore>();
        services.AddScoped<BackgroundEmbeddingIndex>();
        services.AddScoped<IEmbeddingSimilarityReader>(sp => sp.GetRequiredService<PostgresEmbeddingStore>());
        services.AddScoped<IIndexedEmbeddings, IndexedEmbeddingSearch>();
        services.AddScoped<IIndexMaintenance, IndexMaintenance>();
        services.AddHostedService<EmbeddingWorker>();
    }

    private static void RegisterRetrieval(IServiceCollection services)
    {
        services.AddSingleton<LexicalIndexCache>();
        services.AddSingleton<IPassageReranker>(_ => new OnnxPassageReranker(Environment.GetEnvironmentVariable("ALYCIA_RERANK_DIRECTORY")));
        services.AddScoped<IProjectRetrieval, ProjectRetrievalService>();
    }

    private static void RegisterDocuments(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new FileStorage(builder.Configuration["ALYCIA_FILES_DIRECTORY"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "data", "files")));
        builder.Services.AddScoped<ImportSettingsStore>();
        builder.Services.AddScoped<FileImport>();
        builder.Services.AddScoped<DocumentAnalysisService>();
        builder.Services.AddScoped<ProjectSourceReader>();
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = ImportLimits.HardBatchBytes);
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = ImportLimits.HardBatchBytes + 32 * 1024 * 1024);
    }

    private static void RegisterInference(IServiceCollection services, Uri uri)
    {
        services.AddHttpClient<ILocalEmbeddings, OllamaEmbeddings>(http =>
        { http.BaseAddress = uri; http.Timeout = Timeout.InfiniteTimeSpan; http.MaxResponseContentBufferSize = 8 * 1024 * 1024; })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddHttpClient<IChatModel, OllamaChatModel>(http =>
        {
            http.BaseAddress = uri;
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }
}
