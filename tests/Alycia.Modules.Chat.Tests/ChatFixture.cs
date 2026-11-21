using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Workspaces.Infrastructure;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Context;

namespace Alycia.Modules.Chat.Tests;

internal static class ChatFixture
{
    internal static GenerationGate Gate { get; } = new();
    internal static IForegroundActivity Foreground { get; } = new ForegroundActivity();
    internal static InferenceScheduler Scheduler { get; } = new();

    private static readonly ModelTokenizationCache Tokenizers = new();

    public static WorkspaceProfileStore Profiles(ChatDbContext database)
    {
        var session = new RelationalSession(database.Database);
        return new(session, new WorkspaceIdentityDirectory(session));
    }

    public static LiveIndexFileAccess LiveFiles(ChatDbContext database) => new((DbContextOptions<ChatDbContext>)database.GetService<IDbContextOptions>(),
            session => new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session)));

    public static IndexSourceDocumentsAdapter IndexFiles(ChatDbContext database, ProjectSourceReader reader) => new(database,
        (DbContextOptions<ChatDbContext>)database.GetService<IDbContextOptions>(), reader, LiveFiles(database));

    public static KnowledgeStore Knowledge(ChatDbContext database, ProjectSourceReader reader) =>
        new(database, LiveFiles(database), IndexFiles(database, reader));

    public static PostgresEmbeddingStore Embeddings(ChatDbContext database)
    {
        var connection = database.Database.GetConnectionString()!;
        return new(connection, new Alycia.Modules.Memory.MemoryVectorEligibility(connection), transaction =>
        {
            var session = new RelationalSession(transaction);
            return new(new ConversationIndexOwners(session), new WorkspaceProfileStore(session, new WorkspaceIdentityDirectory(session)));
        });
    }

    public static ChatGenerationAdapter Ollama(HttpClient http, ChatOptions options) =>
        new(new OllamaTextGeneration(http, Scheduler, Tokenizers), options);

    public static ChatService Create(IChatRepository repository, IChatModel model, ChatOptions options) =>
        new(repository, model, options, new TurnContext(repository), new ConversationMemory(repository, ChatFixture.Gate),
            new TurnTools(), new TurnRouting(repository), new TurnResponseAudit(), Gate, Foreground);
}
