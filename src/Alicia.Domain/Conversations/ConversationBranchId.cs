namespace Alicia.Domain.Conversations;

public readonly record struct ConversationBranchId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static ConversationBranchId New()
    {
        return new ConversationBranchId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
