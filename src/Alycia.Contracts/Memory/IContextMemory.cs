using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Memory;

public sealed record MemorySelection(string Text, MemoryReport Report);

// The model classifies verbatim quotes. It cannot invent their provenance or erase an earlier claim.
public interface IContextMemory
{
    string PromptVersion { get; }
    MemoryEntry[] Validate(string response, IReadOnlyList<(HistoryMessage Message, string Text)> fragments);
    MemoryLedger Append(MemoryLedger ledger, IEnumerable<MemoryEntry> entries, IReadOnlyList<HistoryMessage> messages, int fallback);
    MemorySelection Select(MemoryLedger ledger, string question, IReadOnlyList<HistoryMessage> recent, int budget, ITextTokenCounter? counter);
    DateTimeOffset? AsOf(string question);
    IEnumerable<TemporalFact> PendingCorrections(IReadOnlyList<HistoryMessage> recent, int sequence, string question);
}
