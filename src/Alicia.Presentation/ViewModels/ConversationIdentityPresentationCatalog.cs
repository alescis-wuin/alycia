using Alicia.Presentation.State;
using Avalonia.Media;

namespace Alicia.Presentation.ViewModels;

internal static class ConversationIdentityPresentationCatalog
{
    private static readonly IBrush _tealAccent = CreateAccent(0x43, 0xE6, 0xD1);
    private static readonly IBrush _tealSurface = CreateSurface(0x43, 0xE6, 0xD1);
    private static readonly IBrush _violetAccent = CreateAccent(0x8C, 0x7C, 0xFF);
    private static readonly IBrush _violetSurface = CreateSurface(0x8C, 0x7C, 0xFF);
    private static readonly IBrush _blueAccent = CreateAccent(0x61, 0xA8, 0xFF);
    private static readonly IBrush _blueSurface = CreateSurface(0x61, 0xA8, 0xFF);
    private static readonly IBrush _greenAccent = CreateAccent(0x72, 0xE6, 0xA7);
    private static readonly IBrush _greenSurface = CreateSurface(0x72, 0xE6, 0xA7);
    private static readonly IBrush _orangeAccent = CreateAccent(0xFF, 0xB4, 0x54);
    private static readonly IBrush _orangeSurface = CreateSurface(0xFF, 0xB4, 0x54);
    private static readonly IBrush _roseAccent = CreateAccent(0xFF, 0x7F, 0xA3);
    private static readonly IBrush _roseSurface = CreateSurface(0xFF, 0x7F, 0xA3);
    private static readonly IBrush _slateAccent = CreateAccent(0x93, 0xA4, 0xC7);
    private static readonly IBrush _slateSurface = CreateSurface(0x93, 0xA4, 0xC7);

    public static IReadOnlyList<(ConversationIdentityIcon Value, string Label)> Icons { get; } =
    [
        (ConversationIdentityIcon.Chat, "Conversation"),
        (ConversationIdentityIcon.Code, "Code"),
        (ConversationIdentityIcon.Idea, "Idea"),
        (ConversationIdentityIcon.Study, "Study"),
        (ConversationIdentityIcon.Research, "Research"),
        (ConversationIdentityIcon.Writing, "Writing"),
        (ConversationIdentityIcon.Creative, "Creative"),
        (ConversationIdentityIcon.Work, "Work"),
    ];

    public static IReadOnlyList<(ConversationIdentityColor Value, string Label)> Colors { get; } =
    [
        (ConversationIdentityColor.Teal, "Teal"),
        (ConversationIdentityColor.Violet, "Violet"),
        (ConversationIdentityColor.Blue, "Blue"),
        (ConversationIdentityColor.Green, "Green"),
        (ConversationIdentityColor.Orange, "Orange"),
        (ConversationIdentityColor.Rose, "Rose"),
        (ConversationIdentityColor.Slate, "Slate"),
    ];

    public static string GetGlyph(ConversationIdentityIcon icon)
    {
        return icon switch
        {
            ConversationIdentityIcon.Chat => "●",
            ConversationIdentityIcon.Code => "{ }",
            ConversationIdentityIcon.Idea => "✦",
            ConversationIdentityIcon.Study => "▤",
            ConversationIdentityIcon.Research => "⌕",
            ConversationIdentityIcon.Writing => "✎",
            ConversationIdentityIcon.Creative => "◆",
            ConversationIdentityIcon.Work => "▣",
            _ => "●",
        };
    }

    public static string GetIconLabel(ConversationIdentityIcon icon)
    {
        return Icons.First(choice => choice.Value == icon).Label;
    }

    public static string GetColorLabel(ConversationIdentityColor color)
    {
        return Colors.First(choice => choice.Value == color).Label;
    }

    public static IBrush GetAccentBrush(ConversationIdentityColor color)
    {
        return color switch
        {
            ConversationIdentityColor.Teal => _tealAccent,
            ConversationIdentityColor.Violet => _violetAccent,
            ConversationIdentityColor.Blue => _blueAccent,
            ConversationIdentityColor.Green => _greenAccent,
            ConversationIdentityColor.Orange => _orangeAccent,
            ConversationIdentityColor.Rose => _roseAccent,
            ConversationIdentityColor.Slate => _slateAccent,
            _ => _tealAccent,
        };
    }

    public static IBrush GetSurfaceBrush(ConversationIdentityColor color)
    {
        return color switch
        {
            ConversationIdentityColor.Teal => _tealSurface,
            ConversationIdentityColor.Violet => _violetSurface,
            ConversationIdentityColor.Blue => _blueSurface,
            ConversationIdentityColor.Green => _greenSurface,
            ConversationIdentityColor.Orange => _orangeSurface,
            ConversationIdentityColor.Rose => _roseSurface,
            ConversationIdentityColor.Slate => _slateSurface,
            _ => _tealSurface,
        };
    }

    private static SolidColorBrush CreateAccent(byte red, byte green, byte blue)
    {
        return new SolidColorBrush(Color.FromRgb(red, green, blue));
    }

    private static SolidColorBrush CreateSurface(byte red, byte green, byte blue)
    {
        return new SolidColorBrush(Color.FromArgb(0x26, red, green, blue));
    }
}
