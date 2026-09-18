namespace Alicia.Domain.Conversations;

public readonly record struct ConversationContextId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static ConversationContextId New()
    {
        return new ConversationContextId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
