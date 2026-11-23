using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

internal sealed class ConversationTestRepository : IChatRepository
{
    public Conversation Conversation { get; } = Conversation.Create("test", "demo", "SYSTEM");
    public List<ChatMessage> Messages { get; } = [];
    public int Saves { get; private set; }
    public ConversationTestRepository(int turns)
    {
        for (var i = 0; i < turns; i++)
        {
            var request = Guid.NewGuid();
            Messages.Add(new(Conversation.Id, i * 2 + 1, request, "user", $"Budget demandé {i:00} : " + new string('u', 240)));
            Messages.Add(new(Conversation.Id, i * 2 + 2, request, "assistant", $"Budget proposé {i:00} : " + new string('a', 240)));
            Conversation.CompleteTurn();
        }
    }
    public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
    public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(Conversation);
    public Task AddAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
    public Task DeleteAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => before == null || m.Sequence < before).TakeLast(limit).ToArray());
    public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid request, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
    public Task AppendTurnAsync(Conversation c, ChatMessage u, ChatMessage a, CancellationToken ct) { Messages.Add(u); Messages.Add(a); return Task.CompletedTask; }
}
