using Alicia.Domain.Conversations;

namespace Alicia.Presentation.ViewModels;

public sealed class ConversationPreviewMessageViewModel
{
    public ConversationPreviewMessageViewModel(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Content = message.Content;
        RoleLabel = message.Role switch
        {
            MessageRole.User => "You",
            MessageRole.Assistant => "Alicia",
            MessageRole.System => "System",
            _ => "Message",
        };
    }

    public string RoleLabel { get; }

    public string Content { get; }
}
