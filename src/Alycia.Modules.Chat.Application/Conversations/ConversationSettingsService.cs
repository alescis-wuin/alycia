namespace Alycia.Modules.Chat.Application.Conversations;

public sealed class ConversationSettingsService(IChatRepository repository, IChatModel model, ChatOptions defaults)
{
    public async Task<(ConversationSummary Conversation, ChatOptions Options)> GetAsync(Guid id, CancellationToken ct)
    {
        var fallback = defaults.Copy();
        var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        return (ConversationProjection.Summary(conversation), ConversationConfiguration.Options(conversation, fallback));
    }

    public async Task<ConversationSummary> UpdateAsync(Guid id, int revision, string modelName, string? systemPrompt,
        string? contextNotes, ChatOptions? options, CancellationToken ct)
    {
        var captured = options?.Copy();
        var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        if (captured is null) throw new ArgumentException("Réglages requis.");
        if (modelName != conversation.Model && !(await model.ListModelsAsync(ct)).Contains(modelName))
            throw new ArgumentException("Choisissez un modèle local capable de générer du texte.");
        conversation.Configure(modelName, systemPrompt, contextNotes, ConversationConfiguration.Serialize(captured));
        captured.Context().ValidatePrompt(ConversationConfiguration.System(conversation), "Bonjour");
        await repository.SaveAsync(ct);
        return ConversationProjection.Summary(conversation);
    }
}
