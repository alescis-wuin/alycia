using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Chat.Tests.Doubles;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Tests;

public sealed class ConversationPortsContractTests
{
    [PostgresFact]
    public Task Conversation_ports_rollback_the_pair_and_commit_memory_at_the_expected_revision() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var project = await ProjectCreationTests.Catalog(db).CreateAsync("p", default);
            var mock = new MemoryChatRepository();
            var real = new EfChatRepository(db, ChatFixture.Profiles(db));
            foreach (var (repo, memory) in new (IChatRepository, IConversationMemory)[] {
                (real,new ConversationMemory(real,new GenerationGate())), (mock,new FixtureConversationMemory(mock)) })
            {
                var owner = Conversation.Create("chat", "local", ""); owner.AssignWorkspace(project.Id);
                var request = Guid.NewGuid(); var id = owner.Id;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.AddAsync(owner, new(true)));
                Assert.Null(await repo.FindAsync(id, default));
                await repo.AddAsync(owner, default);
                foreach (var commit in new[] { false, true })
                {
                    await using var turn = Assert.IsAssignableFrom<IChatTurnScope>(await repo.BeginTurnAsync(id, default));
                    owner = Assert.IsType<Conversation>(await repo.FindAsync(id, default));
                    owner.CompleteTurn();
                    await repo.AppendTurnAsync(owner, new(id, 1, request, "user", "q"), new(id, 2, request, "assistant", "a"), default);
                    if (commit) await turn.CommitAsync(default);
                }
                Assert.Equal(new[] { 1, 2 }, (await repo.FindTurnAsync(id, request, default)).Select(m => m.Sequence));
                Assert.Equal(1, Assert.Single(await repo.MessagesAsync(id, 1, 2, default)).Sequence);
                Assert.Empty(await repo.FindTurnAsync(Guid.Empty, request, default));
                await Assert.ThrowsAsync<ArgumentException>(() => repo.AppendTurnAsync(owner, new(id, 3, Guid.NewGuid(), "user", "q"),
                    new(Guid.NewGuid(), 4, Guid.NewGuid(), "assistant", "a"), default));
                Assert.False(memory.Apply(owner, null));
                Assert.True(memory.Apply(owner, new("souvenir", 2, "hash", null)));
                Assert.True(memory.Apply(owner, new("", 0, "", "{\"schema\":1}")));
                await repo.SaveAsync(default);
                var revision = owner.Revision;
                await Assert.ThrowsAsync<ChatConflictException>(() => memory.ResetAsync(id, revision - 1, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => memory.ResetAsync(id, revision, new(true)));
                var cleared = await memory.ResetAsync(id, revision, default);
                Assert.Equal(("", 0, "{}", 2), (cleared.MemoryText, cleared.MemoryThroughSequence, cleared.MemoryLedgerJson, cleared.MessageCount));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.DeleteAsync(owner, new(true)));
                Assert.Contains(await repo.ListAsync(default), x => x.Id == id);
                await repo.DeleteAsync(cleared, default);
                Assert.Empty(await repo.FindTurnAsync(id, request, default));
            }
        });
}
