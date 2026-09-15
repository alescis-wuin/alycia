namespace Alicia.Presentation.State;

public sealed record ConversationScrollState
{
    public ConversationScrollState(
        ConversationScrollMode mode,
        double verticalOffset)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!double.IsFinite(verticalOffset) || verticalOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalOffset),
                "Conversation scroll offset must be finite and non-negative.");
        }

        Mode = mode;
        VerticalOffset = verticalOffset;
    }

    public ConversationScrollMode Mode { get; }

    public double VerticalOffset { get; }

    public static ConversationScrollState Following { get; } = new(
        ConversationScrollMode.Following,
        verticalOffset: 0);
}
