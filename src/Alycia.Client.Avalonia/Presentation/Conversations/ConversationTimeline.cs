using System.Collections.ObjectModel;
using System.Linq;

namespace Alycia.Client.Avalonia.Presentation.Conversations;

internal sealed class ConversationTimeline
{
    private readonly ObservableCollection<ChatMessageItem> _items = [];
    public ReadOnlyObservableCollection<ChatMessageItem> Messages { get; }
    public int? OldestSequence => _items.Count > 0 ? _items[0].Sequence : null;

    public ConversationTimeline() => Messages = new(_items);

    public void Clear() => _items.Clear();

    public void Replace(MessagePage page)
    {
        _items.Clear();
        foreach (var message in Order(page)) _items.Add(message);
    }

    public void Prepend(MessagePage page)
    {
        var earlier = Order(page);
        for (var i = earlier.Length - 1; i >= 0; i--) _items.Insert(0, earlier[i]);
    }

    private static ChatMessageItem[] Order(MessagePage page) => page.Messages
        .Concat((page.Files ?? []).Select(f => f.ToMessage(page.Conversation.Id)))
        .OrderBy(m => m.Sequence).ThenBy(m => m.IsFile ? 0 : 1).ThenBy(m => m.CreatedAtUtc).ToArray();
}
