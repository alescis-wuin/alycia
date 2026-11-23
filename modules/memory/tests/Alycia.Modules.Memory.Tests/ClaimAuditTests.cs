using Alycia.Contracts.Chat;
using Alycia.Modules.Memory;

namespace Alycia.Modules.Chat.Tests;

public sealed class ClaimAuditTests
{
    [Fact]
    public void MemorySeparatorsCanGroupVariantsButAssistantCannotResolveThem()
    {
        var entries = new[] { new MemoryEntry("base-donnees", "decision", "SQLite", 1, "user", "a"),
            new MemoryEntry("base_donnees", "decision", "Postgres", 2, "assistant", "b") };
        var ledger = new MemoryLedger(1, 2, "hash", entries);
        Assert.Equal(1, StructuredMemory.Select(ledger, "base données", [], 2000).Report.Conflicts);
        Assert.Equal(0, StructuredMemory.Select(ledger, "Correction [base.donnees]: MariaDB", [], 2000).Report.Selected);
        Assert.Equal(2, ledger.Entries.Length);
    }
}
