using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Contracts.Inference;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Memory;

public sealed class ContextMemory : IContextMemory
{
    public string PromptVersion => StructuredMemory.PromptVersion;
    public MemoryEntry[] Validate(string response, IReadOnlyList<(HistoryMessage Message, string Text)> fragments) =>
        StructuredMemory.Validate(response, fragments);
    public MemoryLedger Append(MemoryLedger ledger, IEnumerable<MemoryEntry> entries, IReadOnlyList<HistoryMessage> messages, int fallback) =>
        StructuredMemory.Append(ledger, entries, messages, fallback);
    public MemorySelection Select(MemoryLedger ledger, string question, IReadOnlyList<HistoryMessage> recent, int budget, ITextTokenCounter? counter) =>
        StructuredMemory.Select(ledger, question, recent, budget, counter);
    public DateTimeOffset? AsOf(string question) => TemporalMemory.AsOf(question);
    public IEnumerable<TemporalFact> PendingCorrections(IReadOnlyList<HistoryMessage> recent, int sequence, string question) =>
        recent.SelectMany(m => TemporalMemory.Extract(new TemporalMessage(m.Sequence, m.Role, m.Content, m.CreatedAtUtc))).Where(f => f.Correction)
            .Concat(StructuredMemory.Corrections(sequence, "user", question).Select(e =>
                new TemporalFact(StructuredMemory.CanonicalKey(e.Key), e.Quote, "user", e.Sequence, DateTimeOffset.UtcNow, e.SourceHash, 0, question.Length, true, "correction"))).ToArray();
}
