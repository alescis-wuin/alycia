using Alycia.Modules.Chat.Application.Context;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed class ChatService(IChatRepository repository, IChatModel model, ChatOptions options,
    ITurnContext turnContext, IConversationMemory memory, ITurnTools tools, IProjectRetrieval? retrieval, ITemporalMemory? temporal)
{
    public ChatService(IChatRepository repository, IChatModel model, ChatOptions options,
        IProjectTools? projectTools = null, IProjectRetrieval? retrieval = null, ISnapshotProjectTools? snapshotTools = null,
        ITemporalMemory? temporal = null)
        : this(repository, model, options, new TurnContext(repository), new ConversationMemory(repository),
            new TurnTools(projectTools, snapshotTools), retrieval, temporal)
    { }

    public async Task<Conversation> CreateAsync(string? title, string? modelName, string? systemPrompt,
        CancellationToken cancellationToken, Guid? workspaceId = null)
    {
        var conversation = Conversation.Create(title, modelName, systemPrompt);
        conversation.AssignWorkspace(workspaceId);
        // Validate the system instruction before accepting a conversation.
        SlidingContext.Build(conversation.SystemPrompt, [], "Bonjour", options);
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
        using var lease = GenerationGate.Shared.Enter(id);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Un identifiant de requête est requis.");
        }

        var text = Conversation.RequiredText(content, 32000, "Le message");
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
        var settings = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, options), cancellationToken);
        using var priority = GenerationGate.Shared.PreferForeground();
        var measured = new MeteredChatModel(model);
        if (onDelta is not null && retrieval is not null) await onDelta(new("", Status: "Préparation du contexte…"));
        await QueryRouting.PrepareAsync(repository, conversation, text, settings, retrieval, measured, cancellationToken, temporal);
        var activeTools = await tools.SelectAsync(conversation, settings, model, cancellationToken);
        var budget = await turnContext.AllocateAsync(conversation, text, settings, activeTools is not null, cancellationToken);
        var context = await turnContext.BuildAsync(conversation, text, budget, model, measured, TurnContextMode.Send,
            onDelta is null ? null : status => onDelta(new("", Status: status)), cancellationToken);
        var window = context.Window;
        var generated = await tools.GenerateAsync(measured, activeTools, conversation, window, settings, onDelta, cancellationToken);
        var reply = generated.Content;
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(reply) || reply.Contains('\0'))
        {
            throw new ChatModelException("Ollama a renvoyé une réponse vide ou invalide. Essayez un autre modèle.");
        }

        var citations = CitationAudit.Check(reply, window.Sources, generated.Tools, window.Temporal?.Sources);
        var admitted = citations.Supplied.Where(s => s.Origin != "tool_compacted").Select(s => s.Reference)
            .Concat((citations.Historical ?? []).Select(s => s.Reference)).ToHashSet(StringComparer.Ordinal);
        var suppliedTexts = (generated.EvidenceTexts ?? []).Where(e => admitted.Contains(e.Reference)).Concat(window.MemoryTexts ?? []).ToArray();
        var claims = settings.ClaimVerificationEnabled ? SemanticClaimAudit.Check(reply, window.Sources, suppliedTexts)
            : ClaimAudit.Check(reply, window.Sources, suppliedTexts);
        VerificationFollowup? followup = null;
        if (settings.ClaimVerificationEnabled && claims.Findings.FirstOrDefault(f => f.Status is "insufficient" or "contradicted") is { } gap)
        {
            followup = await SemanticClaimAudit.SearchAsync(gap, id, settings, retrieval, measured, cancellationToken);
            var note = "\n\nVérification après génération — " + (gap.Status == "contradicted" ? "une affirmation contredit le passage cité" : "une affirmation manque de preuve")
                + " : « " + FileContext.Utf8Prefix(gap.Text, 220) + " ».";
            if (followup.Status == "supplementary_support")
                note += " Une recherche complémentaire retrouve cette affirmation dans " + string.Join(", ", followup.References.Select(r => "[" + r + "]")) + ".";
            if (followup.Status == "supplementary_support")
            {
                var extra = CitationAudit.Check(note, followup.Sources, []);
                citations = citations with
                {
                    Supplied = citations.Supplied.Concat(extra.Supplied.Select(s => s with { Origin = "verification" })).DistinctBy(s => s.Reference).ToArray(),
                    Recognized = citations.Recognized.Concat(followup.References).Distinct().ToArray(),
                    Status = citations.Unknown.Length > 0 ? "unknown_references" : "references_recognized"
                };
            }
            reply += note;
            if (onDelta is not null) await onDelta(new(note));
        }
        var user = new ChatMessage(id, conversation.MessageCount + 1, requestId, "user", text);
        var assistant = new ChatMessage(id, conversation.MessageCount + 2, requestId, "assistant", reply);
        if (generated.Tools is { Count: > 0 }) assistant.SetToolTrace(System.Text.Json.JsonSerializer.Serialize(generated.Tools, ProjectToolCatalog.Json));
        memory.Apply(conversation, context.Prepared);
        var usage = measured.Snapshot(settings);
        assistant.SetDiagnostics(System.Text.Json.JsonSerializer.Serialize(new TurnDiagnostics(usage, settings.RetrievalReport, window.Memory, citations, generated.Warnings,
            claims, conversation.WorkspaceId, conversation.ProjectSnapshot?.Revision, settings.Route, window.Assembly, window.Temporal, generated.Research, followup, window.Guides, window.HistoryMessagesIncluded,
            window.Messages.Any(m => m.Role == "system")), ProjectToolCatalog.Json));
        conversation.CompleteTurn();
        // One atomic SaveChanges for both messages and the revision. A failed
        // model call/cancellation never leaves an orphan user message in storage.
        await repository.AppendTurnAsync(conversation, user, assistant, cancellationToken);
        if (snapshot is not null) await snapshot.CommitAsync(cancellationToken);
        return new([user, assistant], window.HistoryMessagesIncluded, false, window.FilesIncluded, window.EstimatedInputTokens, generated.Stats, usage);
    }
}
