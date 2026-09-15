namespace Alicia.Domain.Conversations;

public readonly record struct MessageId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static MessageId New()
    {
        return new MessageId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
