using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")))
        {
            Skip = "Set ALYCIA_CHAT_TEST_CONNECTION to run isolated PostgreSQL integration tests (CREATEDB required).";
        }
    }
}

public sealed class PostgresChatTests
{
    [PostgresFact]
    public async Task MigrationsPersistenceIdempotencyFailureConcurrencyPaginationAndCascade()
    {
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        var databaseName = "alycia_chat_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = databaseName, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
        try
        {
            await using (var schema = new ChatDbContext(options))
            {
                await schema.Database.MigrateAsync();
                await schema.Database.MigrateAsync();
                Assert.Equal(2, (await schema.Database.GetAppliedMigrationsAsync()).Count());
            }

            Guid id;
            var requestId = Guid.NewGuid();
            await using (var database = new ChatDbContext(options))
            {
                var repository = new EfChatRepository(database);
                var service = new ChatService(repository, new FakeModel(), new());
                var conversation = await service.CreateAsync("  Démo  ", "demo", "system", default);
                id = conversation.Id;
                Assert.Equal("Démo", conversation.Title);
                var result = await service.SendAsync(id, requestId, "Bonjour", default);
                Assert.Equal(2, result.Messages.Count);
                var replay = await service.SendAsync(id, requestId, "Bonjour", default);
                Assert.True(replay.Replayed);
                Assert.Equal(2, await database.Messages.CountAsync());
                await Assert.ThrowsAsync<ChatConflictException>(() => service.SendAsync(id, requestId, "different", default));
                var failed = new ChatService(repository, new FakeModel(fail: true), new());
                await Assert.ThrowsAsync<ChatModelException>(() => failed.SendAsync(id, Guid.NewGuid(), "fail", default));
                Assert.Equal(2, await database.Messages.CountAsync());
            }

            // New contexts simulate restart: stored messages are still used as context.
            await using (var database = new ChatDbContext(options))
            {
                var repository = new EfChatRepository(database);
                var model = new FakeModel();
                var service = new ChatService(repository, model, new());
                await service.SendAsync(id, Guid.NewGuid(), "Encore", default);
                Assert.Equal(["system", "Bonjour", "Réponse", "Encore"], model.Prompt!.Select(item => item.Content));
                Assert.Equal(4, (await repository.FindAsync(id, default))!.MessageCount);
                Assert.Equal([3, 4], (await repository.MessagesAsync(id, 2, null, default)).Select(item => item.Sequence));
                Assert.Equal([1, 2], (await repository.MessagesAsync(id, 2, 3, default)).Select(item => item.Sequence));
            }

            await using (var first = new ChatDbContext(options))
            await using (var second = new ChatDbContext(options))
            {
                var firstRepo = new EfChatRepository(first);
                var secondRepo = new EfChatRepository(second);
                var a = (await firstRepo.FindAsync(id, default))!;
                var b = (await secondRepo.FindAsync(id, default))!;
                a.Rename("Renommée");
                await firstRepo.SaveAsync(default);
                // A generation completed against the old revision must roll back both inserts.
                var staleRequest = Guid.NewGuid();
                b.CompleteTurn();
                await Assert.ThrowsAsync<ChatConflictException>(() => secondRepo.AppendTurnAsync(b,
                    new(id, 5, staleRequest, "user", "stale"), new(id, 6, staleRequest, "assistant", "stale"), default));
            }

            await using (var database = new ChatDbContext(options))
            {
                Assert.Equal(4, await database.Messages.CountAsync());
                var repository = new EfChatRepository(database);
                var conversation = (await repository.FindAsync(id, default))!;
                Assert.Equal("Renommée", conversation.Title);
                await repository.DeleteAsync(conversation, default);
                Assert.Empty(await database.Messages.ToListAsync());
                Assert.Empty(await repository.ListAsync(default));
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class FakeModel(bool fail = false) : IChatModel
    {
        public IReadOnlyList<PromptMessage>? Prompt { get; private set; }
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        {
            Prompt = messages;
            return fail ? Task.FromException<string>(new ChatModelException("Unavailable")) : Task.FromResult("Réponse");
        }
    }
}
