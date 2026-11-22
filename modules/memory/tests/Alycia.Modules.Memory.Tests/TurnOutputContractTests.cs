using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;
using Alycia.Modules.Memory;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Chat.Tests;

public sealed class TurnOutputContractTests
{
    [Fact]
    public void Context_memory_reports_only_verbatim_quotes_and_provenance_with_bounded_recall()
    {
        var owner = Guid.NewGuid(); var request = Guid.NewGuid();
        var messages = new[] { new HistoryMessage(owner,1,request,"user","Décision locale : Normandie",DateTimeOffset.UnixEpoch),
            new HistoryMessage(owner,2,request,"assistant","Proposition",DateTimeOffset.UnixEpoch) };
        const string reply = """{"entries":[{"sequence":1,"key":"region","kind":"fact","quote":"Normandie"},{"sequence":1,"key":"region","kind":"fact","quote":"fabriqué"}]}""";
        foreach (IContextMemory memory in new IContextMemory[] { new ContextMemory(), new FixtureContextMemory() })
        {
            Assert.Equal("memory-v2", memory.PromptVersion);
            Assert.Empty(memory.Validate("aucun json", [(messages[0], messages[0].Content)]));
            var entry = Assert.Single(memory.Validate(reply, [(messages[0], messages[0].Content)]));
            Assert.Equal(("Normandie", 1, "user", MemoryTransfer.Hash(messages[0].Content)), (entry.Quote, entry.Sequence, entry.Role, entry.SourceHash));
            var ledger = memory.Append(MemoryTransfer.Empty, [entry, entry], messages, 1);
            Assert.Equal(2, ledger.Through); Assert.Single(ledger.Entries);
            Assert.Equal(0, memory.Select(ledger, "région", [], 0, null).Report.Selected);
            Assert.Contains("Normandie", memory.Select(ledger, "région", [], 2000, null).Text);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), memory.AsOf("au 1970-01-01"));
            Assert.Null(memory.AsOf("récemment"));
            Assert.Equal("user", Assert.Single(memory.PendingCorrections([], 3, "Correction [region]: Bretagne")).Role);
            Assert.Empty(memory.PendingCorrections([messages[1] with { Content = "Correction [region]: Bretagne" }], 3, ""));
        }
    }
}
