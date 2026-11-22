using Alycia.Contracts.Context;

namespace Alycia.Modules.Context.Tests;

public sealed class HistoryWindowTests
{
    [Fact]
    public void Too_large_a_recent_pair_cannot_be_skipped_to_include_an_older_pair()
    {
        var owner = Guid.NewGuid();
        var first = Guid.NewGuid();
        var last = Guid.NewGuid();
        var policy = new SlidingHistory();
        var result = policy.Select([new(owner, first, 1, "user", "a"), new(owner, first, 2, "assistant", "b"),
            new(owner, last, 3, "user", "12345"), new(owner, last, 4, "assistant", "67890")], new(11, 2, 4, 20), text => text.Length);
        Assert.Equal(new HistorySelection(4, 2), result);
        Assert.Throws<InvalidOperationException>(() => policy.Select([new(owner, first, 1, "user", "a"),
            new(Guid.NewGuid(), first, 2, "assistant", "b")], new(11, 2, 4, 20), text => text.Length));
    }
}
