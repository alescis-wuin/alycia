namespace Alicia.Presentation.State;

public sealed record ConversationVisualIdentity
{
    public ConversationVisualIdentity(
        ConversationIdentityIcon icon,
        ConversationIdentityColor color)
    {
        if (!Enum.IsDefined(icon))
        {
            throw new ArgumentOutOfRangeException(nameof(icon));
        }

        if (!Enum.IsDefined(color))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        Icon = icon;
        Color = color;
    }

    public ConversationIdentityIcon Icon { get; }

    public ConversationIdentityColor Color { get; }

    public static ConversationVisualIdentity Default { get; } = new(
        ConversationIdentityIcon.Chat,
        ConversationIdentityColor.Teal);

    public ConversationVisualIdentity WithIcon(ConversationIdentityIcon icon)
    {
        return new ConversationVisualIdentity(icon, Color);
    }

    public ConversationVisualIdentity WithColor(ConversationIdentityColor color)
    {
        return new ConversationVisualIdentity(Icon, color);
    }
}
