using Alicia.Domain.Conversations;

namespace Alicia.Presentation.State;

public sealed class ConversationUiStateSnapshot
{
    private readonly Dictionary<string, ConversationVisualIdentity> _conversationIdentities;
    private readonly Dictionary<string, ConversationScrollState> _conversationScrollStates;

    public ConversationUiStateSnapshot(
        bool isConversationHistoryExpanded,
        IReadOnlyDictionary<string, ConversationScrollState>? conversationScrollStates = null,
        IReadOnlyDictionary<string, ConversationVisualIdentity>? conversationIdentities = null)
    {
        IsConversationHistoryExpanded = isConversationHistoryExpanded;
        _conversationScrollStates = conversationScrollStates is null
            ? new Dictionary<string, ConversationScrollState>(StringComparer.Ordinal)
            : new Dictionary<string, ConversationScrollState>(
                conversationScrollStates,
                StringComparer.Ordinal);
        _conversationIdentities = conversationIdentities is null
            ? new Dictionary<string, ConversationVisualIdentity>(StringComparer.Ordinal)
            : new Dictionary<string, ConversationVisualIdentity>(
                conversationIdentities,
                StringComparer.Ordinal);
    }

    public bool IsConversationHistoryExpanded { get; }

    public IReadOnlyDictionary<string, ConversationScrollState> ConversationScrollStates =>
        _conversationScrollStates;

    public IReadOnlyDictionary<string, ConversationVisualIdentity> ConversationIdentities =>
        _conversationIdentities;

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

    public ConversationVisualIdentity GetConversationIdentity(ConversationId conversationId)
    {
        return _conversationIdentities.TryGetValue(
            conversationId.ToString(),
            out ConversationVisualIdentity? identity)
            ? identity
            : ConversationVisualIdentity.Default;
    }

    public ConversationUiStateSnapshot WithHistoryExpanded(bool isExpanded)
    {
        return new ConversationUiStateSnapshot(
            isExpanded,
            _conversationScrollStates,
            _conversationIdentities);
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

        return new ConversationUiStateSnapshot(
            IsConversationHistoryExpanded,
            updated,
            _conversationIdentities);
    }

    public ConversationUiStateSnapshot WithConversationIdentity(
        ConversationId conversationId,
        ConversationVisualIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Dictionary<string, ConversationVisualIdentity> updated = new(
            _conversationIdentities,
            StringComparer.Ordinal)
        {
            [conversationId.ToString()] = identity,
        };

        return new ConversationUiStateSnapshot(
            IsConversationHistoryExpanded,
            _conversationScrollStates,
            updated);
    }

    public ConversationUiStateSnapshot WithoutConversation(ConversationId conversationId)
    {
        Dictionary<string, ConversationScrollState> updatedScrollStates = new(
            _conversationScrollStates,
            StringComparer.Ordinal);
        updatedScrollStates.Remove(conversationId.ToString());

        Dictionary<string, ConversationVisualIdentity> updatedIdentities = new(
            _conversationIdentities,
            StringComparer.Ordinal);
        updatedIdentities.Remove(conversationId.ToString());

        return new ConversationUiStateSnapshot(
            IsConversationHistoryExpanded,
            updatedScrollStates,
            updatedIdentities);
    }
}
