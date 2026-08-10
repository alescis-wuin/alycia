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

        IsSystem = message.Role == MessageRole.System;
        IsUser = message.Role == MessageRole.User;
        IsAssistant = message.Role == MessageRole.Assistant;
        AutomationName = $"{RoleLabel} message at {CreatedAtLabel}";
    }

    public string RoleLabel { get; }

    public string Content { get; }

    public string CreatedAtLabel { get; }

    public bool IsSystem { get; }

    public bool IsUser { get; }

    public bool IsAssistant { get; }

    public string AutomationName { get; }
}
