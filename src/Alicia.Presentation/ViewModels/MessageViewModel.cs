using System.Globalization;
using Alicia.Domain.Conversations;

namespace Alicia.Presentation.ViewModels;

public sealed class MessageViewModel
{
    public MessageViewModel(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        RoleLabel = message.Role switch
        {
            MessageRole.System => "System",
            MessageRole.User => "You",
            MessageRole.Assistant => "Alicia",
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Role, "Unsupported message role."),
        };

        Content = message.Content;
        CreatedAtLabel = message.CreatedAt
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
    }

    public string RoleLabel { get; }

    public string Content { get; }

    public string CreatedAtLabel { get; }
}
