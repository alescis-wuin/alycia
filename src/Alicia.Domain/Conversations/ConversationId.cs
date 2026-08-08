namespace Alicia.Domain.Conversations;

public readonly record struct ConversationId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static ConversationId New()
    {
        return new ConversationId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
