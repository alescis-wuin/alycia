using Alycia.Contracts.Workspaces;
using Alycia.Platform.Persistence;
using Alycia.Modules.Workspaces.Infrastructure;
using Alycia.Modules.Chat.Application.Context;
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
        builder.Services.AddTransient(_ => options.Copy());
        builder.Services.AddDbContext<ChatDbContext>(db => db.UseNpgsql(connection,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat")));
        RegisterConversations(builder.Services);
        RegisterProjects(builder.Services);
        RegisterDocuments(builder);
        RegisterIndexing(builder.Services);
        RegisterRetrieval(builder.Services);
        RegisterMemory(builder.Services);
        RegisterTools(builder.Services);
        RegisterInference(builder.Services, uri);
    }

    private static void RegisterConversations(IServiceCollection services)
    {
        services.AddScoped<IChatRepository, EfChatRepository>();
        services.AddScoped<ConversationHistory>();
        services.AddScoped<ConversationSettingsService>();
        services.AddScoped<PromptCatalog>();
        services.AddScoped<ConversationContextPreparation>();
        services.AddScoped<ITurnContext, TurnContext>();
        services.AddScoped<ITurnRouting, TurnRouting>();
        services.AddScoped<ITurnResponseAudit, TurnResponseAudit>();
        services.AddScoped(sp => new ChatService(
            sp.GetRequiredService<IChatRepository>(), sp.GetRequiredService<IChatModel>(), sp.GetRequiredService<ChatOptions>(),
            sp.GetRequiredService<ITurnContext>(), sp.GetRequiredService<IConversationMemory>(), sp.GetRequiredService<ITurnTools>(),
            sp.GetRequiredService<ITurnRouting>(), sp.GetRequiredService<ITurnResponseAudit>()));
    }

    private static void RegisterProjects(IServiceCollection services)
    {
        services.AddScoped(sp => new RelationalSession(sp.GetRequiredService<ChatDbContext>().Database));
        services.AddScoped<IWorkspaceIdentityDirectory, WorkspaceIdentityDirectory>();
        services.AddScoped<IProjectCatalog, ProjectCatalog>();
        services.AddScoped<ProjectFiles>();
    }

    private static void RegisterMemory(IServiceCollection services)
    {
        services.AddScoped<IConversationMemory, ConversationMemory>();
        services.AddScoped<ConversationTemporalMemory>();
        services.AddScoped<TemporalMemoryStore>();
        services.AddScoped<ITemporalMemoryIndex>(sp => sp.GetRequiredService<TemporalMemoryStore>());
        services.AddScoped<ITemporalMemory>(sp => sp.GetRequiredService<TemporalMemoryStore>());
    }

    private static void RegisterTools(IServiceCollection services)
    {
        services.AddScoped<ITurnTools, TurnTools>();
        services.AddScoped<ProjectToolService>();
        services.AddScoped<ISnapshotProjectTools>(sp => sp.GetRequiredService<ProjectToolService>());
        services.AddHttpClient<IProjectTools, ProjectMcpClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
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
        builder.Services.AddScoped<ConversationFiles>();
        builder.Services.AddScoped<ConversationFileSettings>();
        builder.Services.AddScoped<DocumentAnalysisService>();
        builder.Services.AddScoped<ProjectSourceReader>();
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = ImportLimits.HardBatchBytes);
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = ImportLimits.HardBatchBytes + 32 * 1024 * 1024);
    }

    private static void RegisterInference(IServiceCollection services, Uri uri)
    {
        services.AddSingleton<InferenceScheduler>();
        services.AddScoped<IChatModel, ChatGenerationAdapter>();
        services.AddHttpClient<ILocalEmbeddings, OllamaEmbeddings>(http =>
        { http.BaseAddress = uri; http.Timeout = Timeout.InfiniteTimeSpan; http.MaxResponseContentBufferSize = 8 * 1024 * 1024; })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddHttpClient<ITextGeneration, OllamaTextGeneration>(http =>
        {
            http.BaseAddress = uri;
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }
}
