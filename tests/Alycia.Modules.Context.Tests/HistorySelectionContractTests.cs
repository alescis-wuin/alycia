using System.Collections.Immutable;
using Alycia.Contracts.Context;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Context.Tests;

public sealed class HistorySelectionContractTests
{
    [Fact]
    public void History_selection_keeps_complete_owner_pairs_within_both_budgets()
    {
        var owner = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        ImmutableArray<WindowMessage> history = [new(owner, a, 1, "user", "a"), new(owner, a, 2, "assistant", "bb"),
            new(owner, b, 3, "user", "ccc"), new(owner, b, 4, "assistant", "dddd")];
        foreach (var provider in new ISlidingHistory[] { new SlidingHistory(), new ReferenceSlidingHistory() })
        {
            var generous = new HistoryBudget(20, 1, 2, 0);
            Assert.Equal(new(0, 11), provider.Select(history, generous, t => t.Length));
            Assert.Equal(new(2, 8), provider.Select(history, generous with { MaxHistoryTokens = 7 }, t => t.Length));
            Assert.Equal(new(4, 1), provider.Select(history, generous with { Available = 7 }, t => t.Length));
            Assert.Equal(new(2, 8), provider.Select(history, generous with { MaxTurns = 1 }, t => t.Length));
            Assert.Throws<ArgumentException>(() => provider.Select(history, generous with { PromptTokens = 21 }, t => t.Length));
            Assert.Throws<InvalidOperationException>(() => provider.Select(history.RemoveAt(0), generous, t => t.Length));
            Assert.Throws<InvalidOperationException>(() => provider.Select(history.SetItem(3, history[3] with { Owner = Guid.NewGuid() }), generous, t => t.Length));
        }
    }
}
