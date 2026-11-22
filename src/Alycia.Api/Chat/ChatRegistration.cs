using Alycia.Modules.Retrieval;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Memory;
using Alycia.Contracts.Context;
using Alycia.Modules.Chat.Application.Conversations;
using Alycia.Contracts.Workspaces;
using Alycia.Platform.Persistence;
using Alycia.Modules.Workspaces.Infrastructure;
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
        builder.Services.AddSingleton(IndexParameters.Capture(options));
        builder.Services.AddDbContext<ChatDbContext>(db => db.UseNpgsql(connection,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat")));
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ChatDbContext>());
        builder.Services.AddScoped<Func<DbContext>>(sp =>
        {
            var options = sp.GetRequiredService<DbContextOptions<ChatDbContext>>();
            return () => new ChatDbContext(options);
        });
        builder.Services.AddSingleton<IConversationLease>(sp => sp.GetRequiredService<GenerationGate>());
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
        services.AddSingleton<IContextMemory, ContextMemory>();
        services.AddSingleton<CreateSummaryReducer>(RecursiveSummarizer.Create);
        services.AddSingleton<IPassageScoring, PassageScoring>();
        services.AddScoped<ITurnContext, TurnContext>();
        services.AddSingleton<Alycia.Contracts.Context.ISlidingHistory, Alycia.Modules.Context.SlidingHistory>();
        services.AddScoped<ConversationContextPreparation>();
    }

    private static void AddAlyciaConversations(IServiceCollection services)
    {
        services.AddSingleton<GenerationGate>();
        services.AddScoped<IChatRepository, EfChatRepository>();
        services.AddScoped<ConversationReadModel>();
        services.AddScoped<IConversationContextReader>(sp => sp.GetRequiredService<ConversationReadModel>());
        services.AddScoped<IAnalysisConversation, AnalysisConversationOwner>();
        services.AddScoped<IConversationFileChanges, ConversationFileChanges>();
        services.AddScoped<ConversationHistory>();
        services.AddScoped<ConversationSettingsService>();
        services.AddScoped<PromptCatalog>();
        services.AddScoped<IQuerySources>(sp => sp.GetRequiredService<ConversationReadModel>());
        services.AddSingleton<CreateQueryBriefRefiner>(_ => model => new QueryBriefRefiner(model));
        services.AddScoped<IQueryPlanner, QueryPlanner>();
        services.AddScoped<ITurnRouter, TurnRouting>();
        services.AddScoped<ITurnResponseAudit, TurnResponseAudit>();
        services.AddScoped(sp => new ChatService(
            sp.GetRequiredService<IChatRepository>(), sp.GetRequiredService<IChatModel>(), sp.GetRequiredService<ChatOptions>(),
            sp.GetRequiredService<ITurnContext>(), sp.GetRequiredService<IConversationMemory>(), sp.GetRequiredService<ITurnTools>(),
            sp.GetRequiredService<ITurnRouter>(), sp.GetRequiredService<ITurnResponseAudit>(),
            sp.GetRequiredService<GenerationGate>(), sp.GetRequiredService<IForegroundActivity>(), sp.GetRequiredService<IModelMeterFactory>(), sp.GetRequiredService<CreateQueryBriefRefiner>()));
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
        services.AddScoped<IToolHistoryReader>(sp => sp.GetRequiredService<ConversationReadModel>());
        services.AddScoped<IToolConversationPolicy, ToolConversationPolicyReader>();
        services.AddScoped<ISourceCommands, Alycia.Modules.Tools.SourceCommands>();
        services.AddScoped<ProjectToolService>();
        services.AddScoped<ISnapshotProjectTools>(sp => sp.GetRequiredService<ProjectToolService>());
        services.AddHttpClient<IProjectTools, ProjectMcpClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }

    private static void AddAlyciaIndexing(IServiceCollection services, string connection)
    {
        services.AddSingleton(sp => new PassageCacheDirectory(sp.GetRequiredService<FileStorage>().Root));
        services.AddScoped(sp => sp.GetRequiredService<ChatDbContext>().Database);
        services.AddScoped<IProjectIndex, ProjectIndexService>();
        services.AddScoped<IndexCatalog>();
        services.AddScoped<ILiveIndexFileAccess>(sp => new LiveIndexFileAccess(sp.GetRequiredService<Func<DbContext>>(),
            session => new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session)), sp.GetRequiredService<DocumentScope>()));
        services.AddScoped<KnowledgeStore>();
        services.AddScoped<IHistoryKnowledge>(sp => sp.GetRequiredService<KnowledgeStore>());
        services.AddScoped<KnowledgeBuilder>();
        services.AddScoped(sp => new PostgresEmbeddingStore(connection, new Alycia.Modules.Memory.MemoryVectorEligibility(connection), (transaction, publishing) =>
        {
            var session = new RelationalSession(transaction);
            return new IndexJobSource(new ConversationIndexOwners(session, publishing), new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session), publishing),
                (owner, project, hashes, ct) => sp.GetRequiredService<DocumentScope>().EligibleHashesAsync(transaction, owner, project, hashes, publishing, ct));
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
        builder.Services.AddSingleton(new DocumentScope(session => new ConversationWorkspaceLookup(session),
            connection => new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connection).Options)));
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
        services.AddScoped<IChatModel>(sp => new ChatGenerationAdapter(sp.GetRequiredService<ITextGeneration>(),
            sp.GetRequiredService<ChatOptions>().InferenceCall()));
        services.AddSingleton<IModelMeterFactory, ModelMeterFactory>();
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
