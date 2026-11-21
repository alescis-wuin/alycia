using Alycia.Contracts.Tools;
using Alycia.Modules.Chat.Application.Context;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed class ChatService(IChatRepository repository, IChatModel model, ChatOptions options,
    ITurnContext turnContext, IConversationMemory memory, ITurnTools tools, ITurnRouter routing, ITurnResponseAudit audit, GenerationGate gate, IForegroundActivity foreground, IModelMeterFactory meters)
{
    public async Task<Conversation> CreateAsync(string? title, string? modelName, string? systemPrompt,
        CancellationToken cancellationToken, Guid? workspaceId = null)
    {
        var conversation = Conversation.Create(title, modelName, systemPrompt);
        conversation.AssignWorkspace(workspaceId);
        // Validate the system instruction before accepting a conversation.
        SlidingContext.ValidatePrompt(conversation.SystemPrompt, "Bonjour", options);
        var models = await model.ListModelsAsync(cancellationToken);
        if (!models.Contains(conversation.Model, StringComparer.Ordinal))
        {
            throw new ArgumentException("Choisissez un modèle local capable de générer du texte, puis actualisez la liste.");
        }

        await repository.AddAsync(conversation, cancellationToken);
        return conversation;
    }

    public Task<SendMessageResult> SendAsync(Guid id, Guid requestId, string? content, CancellationToken cancellationToken)
        => SendCoreAsync(id, requestId, content, null, null, cancellationToken);

    public Task<SendMessageResult> StreamAsync(Guid id, Guid requestId, string? content, int? revision,
        Func<ModelDelta, Task> onDelta, CancellationToken ct)
        => SendCoreAsync(id, requestId, content, revision, onDelta, ct);

    private async Task<SendMessageResult> SendCoreAsync(Guid id, Guid requestId, string? content,
        int? revision, Func<ModelDelta, Task>? onDelta, CancellationToken cancellationToken)
    {
        using var lease = gate.Enter(id);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Un identifiant de requête est requis.");
        }

        var text = Conversation.RequiredText(content, 32000, "Le message");
        var defaults = options.Copy();
        await using var snapshot = await repository.BeginTurnAsync(id, cancellationToken);
        var conversation = await repository.FindAsync(id, cancellationToken)
            ?? throw new ConversationNotFoundException();
        var previous = await repository.FindTurnAsync(id, requestId, cancellationToken);
        if (previous.Count > 0)
        {
            if (previous.Count != 2 || previous[0].Content != text)
            {
                throw new ChatConflictException();
            }

            return new(previous, null, true, Usage: TokenBudget.ReadUsage(previous[1].DiagnosticsJson));
        }

        if (revision.HasValue) ConversationConfiguration.CheckRevision(conversation, revision.Value);
        var settings = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, defaults), cancellationToken);
        using var priority = foreground.PreferForeground();
        var measured = meters.Create(model);
        var routed = await routing.PrepareAsync(conversation.Id, conversation.MessageCount, text, settings.RoutingRequest(),
            new QueryBriefRefiner(measured), onDelta is null ? null : () => onDelta(new("", Status: "Préparation du contexte…")), cancellationToken);
        measured.AddCalls(routed.Calls);
        settings = settings.WithRouting(routed.State);
        var toolScope = new ToolTurnScope(conversation.Id, conversation.WorkspaceId, conversation.Model);
        var activeTools = await tools.SelectAsync(toolScope, settings.UseProjectTools, model, cancellationToken);
        var contextOwner = conversation.ForContext(settings);
        var budget = await turnContext.AllocateAsync(contextOwner, text, settings, activeTools is not null, cancellationToken);
        var context = await turnContext.BuildAsync(contextOwner, text, budget, model, measured, TurnContextMode.Send,
            onDelta is null ? null : status => onDelta(new("", Status: status)), cancellationToken);
        var window = context.Window;
        var generated = await tools.GenerateAsync(measured, activeTools, toolScope, window.ToolPrompt(), settings.ToolGeneration(), onDelta, cancellationToken);
        var review = await audit.ReviewAsync(id, generated, window, settings, measured, onDelta, cancellationToken);
        var user = new ChatMessage(id, conversation.MessageCount + 1, requestId, "user", text);
        var assistant = new ChatMessage(id, conversation.MessageCount + 2, requestId, "assistant", review.Reply);
        TurnResponseAudit.AttachToolTrace(assistant, generated.Tools);
        memory.Apply(conversation, context.Prepared?.MemoryChange());
        var usage = measured.Snapshot(settings.ModelIdentity, settings.TokenCounter);
        TurnResponseAudit.AttachDiagnostics(assistant, conversation, generated, window, settings, usage, review);
        conversation.CompleteTurn();
        // One atomic SaveChanges for both messages and the revision. A failed
        // model call/cancellation never leaves an orphan user message in storage.
        await repository.AppendTurnAsync(conversation, user, assistant, cancellationToken);
        if (snapshot is not null) await snapshot.CommitAsync(cancellationToken);
        return new([user, assistant], window.HistoryMessagesIncluded, false, window.FilesIncluded, window.EstimatedInputTokens, generated.Stats, usage);
    }
}
