namespace Alicia.Domain.Conversations;

public readonly record struct ConversationContextRevisionId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static ConversationContextRevisionId New()
    {
        return new ConversationContextRevisionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
