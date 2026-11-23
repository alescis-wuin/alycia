using Alycia.Platform.Fixtures;
using Alycia.Platform.Persistence;

namespace Alycia.Platform.Tests;

public sealed class RelationalSessionTests
{
    [Fact]
    public void A_completed_external_transaction_cannot_be_enlisted()
    {
        using var transaction = new DetachedTransaction();
        Assert.Throws<ArgumentException>(() => new RelationalSession(transaction));
    }
}
