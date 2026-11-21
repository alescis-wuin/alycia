using Alycia.Modules.Retrieval;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class ClaimAuditTests
{
    [Theory]
    [InlineData("Le quota vaut 37. [S123456789012]", "direct_quote")]
    [InlineData("Le quota vaut 38. [S123456789012]", "uncertain")]
    [InlineData("Le quota vaut 37. [S000000000000]", "not_demonstrated")]
    [InlineData("Le quota vaut 37.", "not_demonstrated")]
    public void KnownMarkerAloneNeverEstablishesTheClaim(string answer, string status)
    {
        var result = ClaimAudit.Check(answer, [], [new("S123456789012", "Le quota vaut 37.")]);
        Assert.Equal(status, Assert.Single(result.Findings).Status);
        Assert.Contains("do not establish truth", result.Policy);
    }
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
