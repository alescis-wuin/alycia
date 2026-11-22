using Alycia.Modules.Inference;
using Alycia.Modules.Retrieval;
using Alycia.Contracts.Indexing;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Workspaces.Infrastructure;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

internal static class ChatFixture
{
    internal static DocumentScope DocumentScope { get; } = new(session => new ConversationWorkspaceLookup(session), Enlist);
    internal static DbContext Enlist(System.Data.Common.DbConnection connection) =>
        new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connection).Options);
    internal static GenerationGate Gate { get; } = new();
    internal static IForegroundActivity Foreground { get; } = new ForegroundActivity();
    internal static InferenceScheduler Scheduler { get; } = new();

    private static readonly ModelTokenizationCache Tokenizers = new();

    public static ProjectRetrievalService Retrieval(IProjectIndex index, IIndexedEmbeddings? semantic = null, LexicalIndexCache? lexicalCache = null, IPassageReranker? reranker = null) =>
        new(new RetrievalPipeline(index, lexicalCache ?? new(), reranker), semantic);

    public static WorkspaceProfileStore Profiles(ChatDbContext database)
    {
        var session = new RelationalSession(database.Database);
        return new(session, new WorkspaceIdentityDirectory(session));
    }

    public static LiveIndexFileAccess LiveFiles(ChatDbContext database) => new(() => new ChatDbContext((DbContextOptions<ChatDbContext>)database.GetService<IDbContextOptions>()),
            session => new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session)), DocumentScope);

    public static IndexSourceDocumentsAdapter IndexFiles(ChatDbContext database, ProjectSourceReader reader) => new(database,
        () => new ChatDbContext((DbContextOptions<ChatDbContext>)database.GetService<IDbContextOptions>()), reader, LiveFiles(database), DocumentScope);

    public static KnowledgeStore Knowledge(ChatDbContext database, ProjectSourceReader reader) =>
        new(new PassageScoring(), database.Database, LiveFiles(database), IndexFiles(database, reader));

    public static PostgresEmbeddingStore Embeddings(ChatDbContext database, string? overrideConnection = null)
    {
        var connection = overrideConnection ?? database.Database.GetConnectionString()!;
        return new(connection, new Alycia.Modules.Memory.MemoryVectorEligibility(connection), (transaction, publishing) =>
        {
            var session = new RelationalSession(transaction);
            return new(new ConversationIndexOwners(session, publishing), new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session), publishing),
                (owner, project, hashes, ct) => DocumentScope.EligibleHashesAsync(transaction, owner, project, hashes, publishing, ct));
        });
    }

    public static ChatGenerationAdapter Ollama(HttpClient http, ChatOptions options) =>
        new(new OllamaTextGeneration(http, Scheduler, Tokenizers), options.InferenceCall());

    public static ChatService Create<T>(T repository, IChatModel model, ChatOptions options)
        where T : IChatRepository, IConversationContextReader, IQuerySources => Create(repository, repository, repository, model, options);

    public static ChatService Create(DbContext database, IChatModel model, ChatOptions options)
    {
        var chat = (ChatDbContext)database;
        var reads = new ConversationReadModel(database, new TurnDocuments(database, DocumentScope));
        return Create(new EfChatRepository(database, Profiles(chat)), reads, reads, model, options);
    }

    private static ChatService Create(IChatRepository repository, IConversationContextReader history, IQuerySources sources, IChatModel model, ChatOptions options) =>
        new(repository, model, options, new TurnContext(new ContextMemory(), history, new Alycia.Modules.Context.SlidingHistory()), new ConversationMemory(repository, ChatFixture.Gate),
            new TurnTools(RecursiveSummarizer.Create), new TurnRouting(new QueryPlanner(sources)), new TurnResponseAudit(), Gate, Foreground, new ModelMeterFactory(), model => new QueryBriefRefiner(model));
}
