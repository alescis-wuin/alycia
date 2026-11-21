using Alycia.Modules.Retrieval;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Memory;
using Alycia.Contracts.Context;
using Alycia.Modules.Chat.Application.Conversations;
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
    public static void AddLocalChat(this WebApplicationBuilder builder, Action<IServiceCollection>? registerMocks)
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
        AddAlyciaContext(builder.Services);
        AddAlyciaConversations(builder.Services);
        AddAlyciaProjects(builder.Services);
        AddAlyciaDocuments(builder);
        AddAlyciaIndexing(builder.Services, connection);
        AddAlyciaRetrieval(builder.Services);
        AddAlyciaMemory(builder.Services);
        AddAlyciaTools(builder.Services);
        AddAlyciaInference(builder.Services, uri, registerMocks);
    }

    private static void AddAlyciaContext(IServiceCollection services)
    {
        services.AddScoped<ITurnContext, TurnContext>();
        services.AddSingleton<Alycia.Contracts.Context.ISlidingHistory, Alycia.Modules.Context.SlidingHistory>();
        services.AddScoped<ConversationContextPreparation>();
    }

    private static void AddAlyciaConversations(IServiceCollection services)
    {
        services.AddSingleton<GenerationGate>();
        services.AddScoped<IChatRepository, EfChatRepository>();
        services.AddScoped<IConversationContextReader>(sp => sp.GetRequiredService<IChatRepository>());
        services.AddScoped<IAnalysisConversation, AnalysisConversationOwner>();
        services.AddScoped<IConversationFileChanges, ConversationFileChanges>();
        services.AddScoped<ConversationHistory>();
        services.AddScoped<ConversationSettingsService>();
        services.AddScoped<PromptCatalog>();
        services.AddScoped<IQuerySources>(sp => sp.GetRequiredService<IChatRepository>());
        services.AddScoped<IQueryPlanner, QueryPlanner>();
        services.AddScoped<ITurnRouter, TurnRouting>();
        services.AddScoped<ITurnResponseAudit, TurnResponseAudit>();
        services.AddScoped(sp => new ChatService(
            sp.GetRequiredService<IChatRepository>(), sp.GetRequiredService<IChatModel>(), sp.GetRequiredService<ChatOptions>(),
            sp.GetRequiredService<ITurnContext>(), sp.GetRequiredService<IConversationMemory>(), sp.GetRequiredService<ITurnTools>(),
            sp.GetRequiredService<ITurnRouter>(), sp.GetRequiredService<ITurnResponseAudit>(),
            sp.GetRequiredService<GenerationGate>(), sp.GetRequiredService<IForegroundActivity>()));
    }

    private static void AddAlyciaProjects(IServiceCollection services)
    {
        services.AddScoped(sp => new RelationalSession(sp.GetRequiredService<ChatDbContext>().Database));
        services.AddScoped<IWorkspaceIdentityDirectory, WorkspaceIdentityDirectory>();
        services.AddScoped<WorkspaceProfileStore>();
        services.AddScoped<IWorkspaceProfiles>(sp => sp.GetRequiredService<WorkspaceProfileStore>());
        services.AddScoped<IProjectNoteOrigins, ProjectNoteOrigins>();
        services.AddScoped<IDocumentMembershipOverrides, DocumentMembershipOverrides>();
        services.AddScoped<ConversationMembership>();
        services.AddScoped<IProjectCatalog, ProjectCatalog>();
        services.AddScoped<IProjectModelDefaults>(sp => sp.GetRequiredService<IProjectCatalog>());
        services.AddScoped<ConversationCreation>();
        services.AddScoped<ProjectFiles>();
    }

    private static void AddAlyciaMemory(IServiceCollection services)
    {
        services.AddScoped<IConversationMemory, ConversationMemory>();
        services.AddScoped<ConversationTemporalMemory>();
        services.AddScoped<Alycia.Contracts.Memory.ITemporalFactStorage>(sp => new Alycia.Modules.Memory.TemporalFactStorage(sp.GetRequiredService<ChatDbContext>().Database));
        services.AddScoped<Alycia.Contracts.Memory.ITemporalHistorySources, TemporalHistorySources>();
        services.AddScoped<TemporalMemoryStore>();
        services.AddScoped<ITemporalMemoryIndex>(sp => sp.GetRequiredService<TemporalMemoryStore>());
        services.AddScoped<ITemporalMemory>(sp => sp.GetRequiredService<TemporalMemoryStore>());
    }

    private static void AddAlyciaTools(IServiceCollection services)
    {
        services.AddScoped<ITurnTools, TurnTools>();
        services.AddScoped<IToolHistoryReader>(sp => sp.GetRequiredService<IChatRepository>());
        services.AddScoped<IToolConversationPolicy, ToolConversationPolicyReader>();
        services.AddScoped<ISourceCommands, Alycia.Modules.Tools.SourceCommands>();
        services.AddScoped<ProjectToolService>();
        services.AddScoped<ISnapshotProjectTools>(sp => sp.GetRequiredService<ProjectToolService>());
        services.AddHttpClient<IProjectTools, ProjectMcpClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }

    private static void AddAlyciaIndexing(IServiceCollection services, string connection)
    {
        services.AddScoped<IProjectIndex, ProjectIndexService>();
        services.AddScoped<IndexCatalog>();
        services.AddScoped<ILiveIndexFileAccess>(sp => new LiveIndexFileAccess(sp.GetRequiredService<DbContextOptions<ChatDbContext>>(),
            session => new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session)), sp.GetRequiredService<DocumentScope>()));
        services.AddScoped<KnowledgeStore>();
        services.AddScoped<IHistoryKnowledge>(sp => sp.GetRequiredService<KnowledgeStore>());
        services.AddScoped<KnowledgeBuilder>();
        services.AddScoped(_ => new PostgresEmbeddingStore(connection, new Alycia.Modules.Memory.MemoryVectorEligibility(connection), transaction =>
        {
            var session = new RelationalSession(transaction);
            return new IndexJobSource(new ConversationIndexOwners(session), new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session)));
        }));
        services.AddScoped<IConversationIndexOwners, ConversationIndexOwners>();
        services.AddScoped<IProjectIndexOwners>(sp => sp.GetRequiredService<WorkspaceProfileStore>());
        services.AddScoped<BackgroundEmbeddingIndex>();
        services.AddScoped<IEmbeddingSimilarityReader>(sp => sp.GetRequiredService<PostgresEmbeddingStore>());
        services.AddScoped<IIndexedEmbeddings, IndexedEmbeddingSearch>();
        services.AddScoped<IIndexMaintenance, IndexMaintenance>();
        services.AddHostedService<EmbeddingWorker>();
    }

    private static void AddAlyciaRetrieval(IServiceCollection services)
    {
        services.AddSingleton<LexicalIndexCache>();
        services.AddSingleton<IPassageReranker>(_ => new OnnxPassageReranker(Environment.GetEnvironmentVariable("ALYCIA_RERANK_DIRECTORY")));
        services.AddScoped<IRetrievalPipeline, Alycia.Modules.Retrieval.RetrievalPipeline>();
        services.AddScoped<IProjectRetrieval, ProjectRetrievalService>();
    }

    private static void AddAlyciaDocuments(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new DocumentScope(session => new ConversationWorkspaceLookup(session)));
        builder.Services.AddScoped<ITurnDocuments, TurnDocuments>();
        builder.Services.AddScoped<IConversationOriginals, ConversationOriginals>();
        builder.Services.AddScoped<IArchiveDocuments, ArchiveDocuments>();
        builder.Services.AddScoped<IAuthorizedToolSources, AuthorizedToolSources>();
        builder.Services.AddScoped<IIndexSourceDocuments, IndexSourceDocumentsAdapter>();
        builder.Services.AddSingleton(new FileStorage(builder.Configuration["ALYCIA_FILES_DIRECTORY"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "data", "files")));
        builder.Services.AddScoped<ImportSettingsStore>();
        builder.Services.AddScoped<IImportLimitsReader>(services => services.GetRequiredService<ImportSettingsStore>());
        builder.Services.AddScoped<FileImport>();
        builder.Services.AddScoped<ConversationFiles>();
        builder.Services.AddScoped<ConversationFileSettings>();
        builder.Services.AddScoped<DocumentAnalysisService>();
        builder.Services.AddScoped<ProjectSourceReader>();
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = ImportLimits.HardBatchBytes);
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = ImportLimits.HardBatchBytes + 32 * 1024 * 1024);
    }

    private static void AddAlyciaInference(IServiceCollection services, Uri uri, Action<IServiceCollection>? registerMocks)
    {
        services.AddSingleton<InferenceScheduler>();
        services.AddSingleton<ModelTokenizationCache>();
        services.AddSingleton<IForegroundActivity, ForegroundActivity>();
        services.AddScoped<IChatModel, ChatGenerationAdapter>();
        if (registerMocks is not null)
        {
            registerMocks(services);
            return;
        }
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
