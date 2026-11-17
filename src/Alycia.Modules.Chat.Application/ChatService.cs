using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed partial class ChatService(IChatRepository repository, IChatModel model, ChatOptions options, IProjectTools? projectTools = null, IProjectRetrieval? retrieval = null)
{
    public async Task<Conversation> CreateAsync(string? title, string? modelName, string? systemPrompt,
        CancellationToken cancellationToken)
    {
        var conversation = Conversation.Create(title, modelName, systemPrompt);
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
        var measured = new MeteredChatModel(model);
        if (retrieval is not null && settings.RetrievalMode != "legacy")
        {
            if (onDelta is not null) await onDelta(new("", Status: "Recherche des sources pertinentes…"));
            var sources = await retrieval.PrepareAsync(id, text, settings, cancellationToken);
            settings.SourceCandidates = sources.Chunks; settings.RetrievalReport = sources.Report;
            measured.AddCalls(sources.Calls);
        }
        var useTools = settings.UseProjectTools && projectTools is not null && await projectTools.HasSourcesAsync(id, cancellationToken)
            && await model.SupportsToolsAsync(conversation.Model, cancellationToken);
        var contextSettings = useTools ? ProjectAgent.ContextOptions(settings) : settings;
        PreparedContext? prepared = null;
        ContextWindow window;
        if (settings.AutoSummarize)
        {
            prepared = await ConversationContext.BuildAsync(repository, measured, conversation, text, contextSettings, true,
                onDelta is null ? null : status => onDelta(new("", Status: status)), cancellationToken);
            window = prepared.Context;
        }
        else
        {
            var history = await repository.MessagesAsync(id, settings.MaxHistoryTurns * 2, null, cancellationToken);
            var files = await repository.ContextFilesAsync(id, Math.Max(1, conversation.MessageCount - settings.MaxHistoryTurns * 2 + 1), cancellationToken);
            window = FileContext.Build(ConversationConfiguration.System(conversation), history, text, contextSettings, files, conversation.MessageCount + 1);
        }
        if (useTools) window = window with { ToolReserve = ProjectAgent.Reserve(settings) };
        var generated = useTools ? await ProjectAgent.RunAsync(measured, projectTools!, id, conversation.Model, window, settings, onDelta, cancellationToken)
            : onDelta is null
            ? new GenerationReply(await measured.GenerateAsync(conversation.Model, window.Messages, settings, cancellationToken))
            : await measured.StreamAsync(conversation.Model, window.Messages, settings, onDelta, cancellationToken);
        var reply = generated.Content;
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(reply) || reply.Contains('\0'))
        {
            throw new ChatModelException("Ollama a renvoyé une réponse vide ou invalide. Essayez un autre modèle.");
        }

        var user = new ChatMessage(id, conversation.MessageCount + 1, requestId, "user", text);
        var assistant = new ChatMessage(id, conversation.MessageCount + 2, requestId, "assistant", reply);
        if (generated.Tools is { Count: > 0 }) assistant.SetToolTrace(System.Text.Json.JsonSerializer.Serialize(generated.Tools, ProjectToolCatalog.Json));
        if (prepared is { Changed: true })
        {
            if (prepared.LedgerJson is not null) conversation.SetMemoryLedger(prepared.LedgerJson);
            else conversation.SetMemory(prepared.Memory, prepared.Through, prepared.SourceHash, conversation.Model);
        }
        var usage = measured.Snapshot(settings);
        assistant.SetDiagnostics(System.Text.Json.JsonSerializer.Serialize(new { usage, retrieval = settings.RetrievalReport, memory = window.Memory }, ProjectToolCatalog.Json));
        conversation.CompleteTurn();
        // One atomic SaveChanges for both messages and the revision. A failed
        // model call/cancellation never leaves an orphan user message in storage.
        await repository.AppendTurnAsync(conversation, user, assistant, cancellationToken);
        return new([user, assistant], window.HistoryMessagesIncluded, false, window.FilesIncluded, window.EstimatedInputTokens, generated.Stats, usage);
    }
}
