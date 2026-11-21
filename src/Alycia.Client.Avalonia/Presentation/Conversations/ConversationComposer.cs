using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Presentation.Conversations;

internal sealed class ConversationComposer(DraftStore drafts)
{
    public ConversationSubmission? Pending { get; private set; }
    public string? Error => drafts.Error;
    public string TextFor(Guid? owner) => owner.HasValue ? drafts.Get(owner.Value)?.Text ?? "" : "";
    public void Flush() => drafts.Save();
    public void Remove(Guid owner) => drafts.Remove(owner);

    public void Edit(Guid? owner, string? text)
    {
        if (owner.HasValue && Pending is null) drafts.Set(owner.Value, text ?? "");
    }

    public ConversationSubmission? Begin(ConversationSummary? conversation, string? input)
    {
        if (Pending is not null) throw new InvalidOperationException("Un envoi est déjà en cours.");
        var text = input?.Trim();
        if (conversation is null || string.IsNullOrWhiteSpace(text)) return null;
        var draft = drafts.Set(conversation.Id, text);
        drafts.Save();
        return Pending = new(conversation, draft);
    }

    public async Task<ChatSendResult> SendAsync(ChatApiClient client, Func<ModelDelta, Task> delta, CancellationToken ct)
    {
        var submission = Pending ?? throw new InvalidOperationException("Aucun envoi en cours.");
        var result = await client.StreamMessageAsync(submission.Conversation.Id, submission.Draft.RequestId, submission.Draft.Text,
            submission.Conversation.Revision, delta, ct);
        // A refresh after the server acknowledgement must never turn this back into a pending send.
        submission.Confirmed = true;
        drafts.Remove(submission.Conversation.Id);
        drafts.Save();
        return result;
    }

    public string? RestoreIfEmpty(Guid? owner, string? editorText)
    {
        if (Pending is not { Confirmed: false } submission || submission.Conversation.Id != owner || !string.IsNullOrWhiteSpace(editorText)) return null;
        drafts.Set(submission.Conversation.Id, submission.Draft.Text);
        drafts.Save();
        return submission.Draft.Text;
    }

    public void End() => Pending = null;
}

internal sealed class ConversationSubmission(ConversationSummary conversation, DraftEntry draft)
{
    private readonly StringBuilder _output = new();
    private DateTime _lastRender = DateTime.UtcNow;
    private bool _thinking;
    public ConversationSummary Conversation { get; } = conversation;
    public DraftEntry Draft { get; } = draft;
    public bool Confirmed { get; set; }
    public string Output => _output.ToString();
    public string? GenerationStatus => _output.Length > 0 ? $"Génération en cours · {_output.Length:N0} caractères" : _thinking ? "Le modèle réfléchit…" : null;

    public bool Append(ModelDelta delta)
    {
        _output.Append(delta.Content);
        _thinking |= !string.IsNullOrEmpty(delta.Thinking);
        var now = DateTime.UtcNow;
        if (_output.Length > 0 && (now - _lastRender).TotalMilliseconds < 80) return false;
        _lastRender = now;
        return true;
    }
}
