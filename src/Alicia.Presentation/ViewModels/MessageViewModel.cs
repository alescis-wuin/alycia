using System.Globalization;
using Alicia.Domain.Conversations;

namespace Alicia.Presentation.ViewModels;

public sealed class MessageViewModel : ViewModelBase
{
    private string _content;

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

        _content = message.Content;
        CreatedAtLabel = message.CreatedAt
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);

        IsSystem = message.Role == MessageRole.System;
        IsUser = message.Role == MessageRole.User;
        IsAssistant = message.Role == MessageRole.Assistant;
        AutomationName = $"{RoleLabel} message at {CreatedAtLabel}";
    }

    private MessageViewModel()
    {
        RoleLabel = "Alicia";
        _content = string.Empty;
        CreatedAtLabel = "Streaming…";
        IsAssistant = true;
        IsStreaming = true;
        AutomationName = "Alicia response in progress";
    }

    public string RoleLabel { get; }

    public string Content
    {
        get => _content;
        private set => SetProperty(ref _content, value);
    }

    public string CreatedAtLabel { get; }

    public bool IsSystem { get; }

    public bool IsUser { get; }

    public bool IsAssistant { get; }

    public bool IsStreaming { get; }

    public string AutomationName { get; }

    public static MessageViewModel CreateStreamingAssistant()
    {
        return new MessageViewModel();
    }

    public void AppendContentDelta(string contentDelta)
    {
        ArgumentNullException.ThrowIfNull(contentDelta);

        if (!IsStreaming)
        {
            throw new InvalidOperationException(
                "Only a streaming assistant projection can accept response deltas.");
        }

        if (contentDelta.Length == 0)
        {
            throw new ArgumentException(
                "Streaming content delta cannot be empty.",
                nameof(contentDelta));
        }

        Content += contentDelta;
    }
}
