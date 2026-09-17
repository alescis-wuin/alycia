namespace Alicia.Domain.Conversations;

public readonly record struct MessageRevisionId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static MessageRevisionId New()
    {
        return new MessageRevisionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
