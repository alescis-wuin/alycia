namespace Alicia.Domain.Conversations;

public readonly record struct GenerationSnapshotId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static GenerationSnapshotId New()
    {
        return new GenerationSnapshotId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
