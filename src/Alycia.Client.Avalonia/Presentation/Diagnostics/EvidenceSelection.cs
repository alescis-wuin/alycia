using System;
using System.Collections.Generic;
using System.Linq;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

internal sealed class EvidenceSelection
{
    public Guid Conversation { get; }
    public ChatMessageItem? Message { get; }
    public TurnDiagnostics? Diagnostics { get; }

    public EvidenceSelection(Guid conversation, ChatMessageItem? selected, IEnumerable<ChatMessageItem> messages)
    {
        Conversation = conversation;
        Message = selected is { IsAssistant: true } && selected.ConversationId == conversation ? selected
            : messages.LastOrDefault(m => m.IsAssistant && m.ConversationId == conversation);
        Diagnostics = Message is null ? null : TurnDiagnostics.Read(Message.DiagnosticsJson);
    }

    public SuppliedSource? Resolve(ChatMessageItem origin, string reference)
    {
        if (origin != Message) return null;
        return Diagnostics?.Citations?.Supplied.FirstOrDefault(s =>
            string.Equals(s.Reference, reference, StringComparison.OrdinalIgnoreCase));
    }
}
