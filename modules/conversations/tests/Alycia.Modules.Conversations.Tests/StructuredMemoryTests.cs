using System.Text.Json;
using Alycia.Contracts.Context;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class StructuredMemoryTests
{
    [Fact]
    public void PreparedMemoryRetainsItsFormatAndWaitsForTheCallersCommit()
    {
        var repo = new ConversationTestRepository(1);
        var conversation = repo.Conversation;
        var gate = new GenerationGate();
        var memory = new ConversationMemory(repo, gate);
        var prepared = new PreparedContext(new([], 0, 0), "Résumé du premier tour", 2, "source", true);
        var revision = conversation.Revision;
        Assert.False(memory.Apply(conversation, null));
        Assert.False(memory.Apply(conversation, (prepared with { Changed = false }).MemoryChange()));
        Assert.Equal(revision, conversation.Revision);

        Assert.True(memory.Apply(conversation, prepared.MemoryChange()));
        Assert.Equal(("Résumé du premier tour", 2, "source", "demo"),
            (conversation.MemoryText, conversation.MemoryThroughSequence, conversation.MemorySourceHash, conversation.MemoryModel));
        Assert.Equal("{}", conversation.MemoryLedgerJson);
        var ledger = JsonSerializer.Serialize(new MemoryLedger(1, 2, "chain", []), ProjectToolCatalog.Json);
        Assert.True(memory.Apply(conversation, (prepared with { LedgerJson = ledger, Memory = "ignoré" }).MemoryChange()));
        Assert.Equal(ledger, conversation.MemoryLedgerJson);
        Assert.Equal("Résumé du premier tour", conversation.MemoryText);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task ResetChecksTheGateAndRevisionBeforeClearingBothMemoryFormatsTogether()
    {
        var repo = new ConversationTestRepository(1);
        var conversation = repo.Conversation;
        conversation.SetMemory("résumé", 2, "source", "demo");
        var ledger = JsonSerializer.Serialize(new MemoryLedger(1, 2, "chain", []), ProjectToolCatalog.Json);
        conversation.SetMemoryLedger(ledger);
        var revision = conversation.Revision;
        var gate = new GenerationGate();
        var memory = new ConversationMemory(repo, gate);
        using (gate.Enter(conversation.Id))
            await Assert.ThrowsAsync<ChatConflictException>(() => memory.ResetAsync(conversation.Id, revision, default));
        await Assert.ThrowsAsync<ChatConflictException>(() => memory.ResetAsync(conversation.Id, revision - 1, default));
        Assert.Equal((revision, "résumé", ledger, 0), (conversation.Revision, conversation.MemoryText, conversation.MemoryLedgerJson, repo.Saves));

        var reset = await memory.ResetAsync(conversation.Id, revision, default);
        Assert.Equal(revision + 2, reset.Revision);
        Assert.Equal(("", 0, "", "", "{}"),
            (reset.MemoryText, reset.MemoryThroughSequence, reset.MemorySourceHash, reset.MemoryModel, reset.MemoryLedgerJson));
        Assert.Equal(2, reset.MessageCount);
        Assert.Equal(1, repo.Saves);
    }
}
