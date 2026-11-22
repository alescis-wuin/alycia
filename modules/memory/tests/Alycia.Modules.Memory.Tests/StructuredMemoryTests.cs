using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;
using Alycia.Modules.Memory;

namespace Alycia.Modules.Chat.Tests;

public sealed class StructuredMemoryTests
{
    [Fact]
    public void TemporalProjectionRetainsOnlySourceBackedLedgerEvidence()
    {
        var source = new TemporalMessage(2, "assistant", "Lieu envisagé : Normandie.", DateTimeOffset.UtcNow);
        var original = new MemoryEntry("Région", "fact", "Normandie", 2, "assistant", StructuredMemory.Hash(source.Content), Correction: true);
        var facts = TemporalMemory.IndexFacts(source,
        [
            original, original, original with { Key = "signature", SourceHash = "autre" }, original with { Key = "inversé", Role = "user" },
            original with { Key = "séquence", Sequence = 4 }, original with { Key = "inventé", Quote = "Bretagne" },
            original with { Key = "secours", Fallback = true }
        ]);
        Assert.Equal(2, facts.Length);
        var confirmed = Assert.Single(facts, f => f.Key == StructuredMemory.CanonicalKey(original.Key));
        Assert.Equal(original.Quote, source.Content.Substring(confirmed.Offset, confirmed.Quote.Length));
        Assert.Equal((source.Role, source.CreatedAtUtc, source.Sequence, source.Content.Length, original.SourceHash, false, "proposition"),
            (confirmed.Role, confirmed.Observed, confirmed.Sequence, confirmed.FullLength, confirmed.Sha256, confirmed.Correction, confirmed.Kind));
    }

    [Fact]
    public void RejectsFabricatedQuotesAndKeepsContradictionsUntilUserCorrection()
    {
        var id = Guid.NewGuid();
        var source = new HistoryMessage(id, 1, Guid.NewGuid(), "user", "La base est SQLite.", DateTimeOffset.UtcNow);
        var entries = StructuredMemory.Validate("""
            {"entries":[{"key":"base","kind":"fact","sequence":1,"quote":"La base est SQLite."},
            {"key":"base","kind":"fact","sequence":1,"quote":"La base est PostgreSQL."}]}
            """, [(source, source.Content)]);
        Assert.Single(entries);
        Assert.Equal(StructuredMemory.Hash(source.Content), entries[0].SourceHash);
        var other = new MemoryEntry("base", "fact", "La base est PostgreSQL.", 3, "user", "hash");
        var ledger = new MemoryLedger(1, 4, "hash", [entries[0], other]);
        var selected = StructuredMemory.Select(ledger, "base", [], 2000);
        Assert.Equal(1, selected.Report.Conflicts);
        Assert.Contains("SQLite", selected.Text); Assert.Contains("PostgreSQL", selected.Text);
        Assert.Equal(0, StructuredMemory.Select(ledger, "base", [], 100).Report.Selected);
        var assistant = new HistoryMessage(id, 6, Guid.NewGuid(), "assistant", "Correction [base]: MySQL", DateTimeOffset.UtcNow);
        Assert.Equal(1, StructuredMemory.Select(ledger, "base", [assistant], 2000).Report.Conflicts);
        var corrected = StructuredMemory.Select(ledger, "Correction [base]: MariaDB", [], 2000);
        Assert.Equal(0, corrected.Report.Selected);
        Assert.DoesNotContain("SQLite", corrected.Text);
    }
}
