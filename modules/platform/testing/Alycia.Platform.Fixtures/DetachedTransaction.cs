using System.Data;
using System.Data.Common;

namespace Alycia.Platform.Fixtures;

public sealed class DetachedTransaction : DbTransaction
{
    public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
    protected override DbConnection? DbConnection => null;
    public override void Commit() => throw new InvalidOperationException();
    public override void Rollback() => throw new InvalidOperationException();
}
