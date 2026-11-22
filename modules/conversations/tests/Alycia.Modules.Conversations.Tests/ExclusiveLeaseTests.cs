using Alycia.Contracts.Chat;
using Alycia.Contracts.Documents;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Conversations.Tests;

public sealed class ExclusiveLeaseTests
{
    [Fact]
    public void Reservations_are_owner_scoped_and_old_disposal_cannot_release_the_new_holder()
    {
        foreach (IConversationLease gate in new IConversationLease[] { new GenerationGate(), new MemoryConversationLeases() })
        {
            var owner = Guid.NewGuid();
            var first = gate.Enter(owner);
            using var unrelated = gate.Enter(Guid.NewGuid());
            Assert.Throws<ChatConflictException>(() => gate.Enter(owner));
            first.Dispose();
            using var second = gate.Enter(owner);
            first.Dispose();
            Assert.Throws<ChatConflictException>(() => gate.Enter(owner));
        }
    }
}
