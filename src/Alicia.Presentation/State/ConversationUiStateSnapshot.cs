using Alicia.Domain.Conversations;

namespace Alicia.Presentation.State;

public sealed class ConversationUiStateSnapshot
{
    private readonly Dictionary<string, ConversationScrollState> _conversationScrollStates;

    public ConversationUiStateSnapshot(
        bool isConversationHistoryExpanded,
        IReadOnlyDictionary<string, ConversationScrollState>? conversationScrollStates = null)
    {
        IsConversationHistoryExpanded = isConversationHistoryExpanded;
        _conversationScrollStates = conversationScrollStates is null
            ? new Dictionary<string, ConversationScrollState>(StringComparer.Ordinal)
            : new Dictionary<string, ConversationScrollState>(
                conversationScrollStates,
                StringComparer.Ordinal);
    }

    public bool IsConversationHistoryExpanded { get; }

    public IReadOnlyDictionary<string, ConversationScrollState> ConversationScrollStates =>
        _conversationScrollStates;

    public static ConversationUiStateSnapshot Default { get; } = new(
        isConversationHistoryExpanded: true);

    public ConversationScrollState GetConversationScrollState(ConversationId conversationId)
    {
        return _conversationScrollStates.TryGetValue(
            conversationId.ToString(),
            out ConversationScrollState? state)
            ? state
            : ConversationScrollState.Following;
    }

    public ConversationUiStateSnapshot WithHistoryExpanded(bool isExpanded)
    {
        return new ConversationUiStateSnapshot(isExpanded, _conversationScrollStates);
    }

    public ConversationUiStateSnapshot WithConversationScrollState(
        ConversationId conversationId,
        ConversationScrollState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Dictionary<string, ConversationScrollState> updated = new(
            _conversationScrollStates,
            StringComparer.Ordinal)
        {
            [conversationId.ToString()] = state,
        };

        return new ConversationUiStateSnapshot(IsConversationHistoryExpanded, updated);
    }

    public ConversationUiStateSnapshot WithoutConversation(ConversationId conversationId)
    {
        Dictionary<string, ConversationScrollState> updated = new(
            _conversationScrollStates,
            StringComparer.Ordinal);
        updated.Remove(conversationId.ToString());
        return new ConversationUiStateSnapshot(IsConversationHistoryExpanded, updated);
    }
}
