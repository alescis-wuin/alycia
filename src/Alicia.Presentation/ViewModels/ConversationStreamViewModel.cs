using System.Collections.ObjectModel;
using Alicia.Domain.Conversations;

namespace Alicia.Presentation.ViewModels;

public sealed class ConversationStreamViewModel : ViewModelBase
{
    private bool _isGeneratingResponse;
    private bool _isSendingMessage;

    public ConversationStreamViewModel(bool isReducedMotionEnabled)
    {
        IsReducedMotionEnabled = isReducedMotionEnabled;
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    public bool IsGeneratingResponse
    {
        get => _isGeneratingResponse;
        internal set => SetProperty(ref _isGeneratingResponse, value);
    }

    public bool IsSendingMessage
    {
        get => _isSendingMessage;
        internal set => SetProperty(ref _isSendingMessage, value);
    }

    public bool IsReducedMotionEnabled { get; }

    internal MessageViewModel AddStreamingAssistant()
    {
        MessageViewModel message = MessageViewModel.CreateStreamingAssistant();
        Messages.Add(message);
        return message;
    }

    internal bool Remove(MessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Messages.Remove(message);
    }

    internal void LoadMessages(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        Messages.Clear();

        foreach (ChatMessage message in messages)
        {
            Messages.Add(new MessageViewModel(message));
        }
    }

    internal void ClearMessages()
    {
        Messages.Clear();
    }
}
