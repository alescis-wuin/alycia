using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application.Context;

public sealed class ConversationSettingsService(IChatRepository repository, IChatModel model, ChatOptions defaults)
{
    public async Task<(Conversation Conversation, ChatOptions Options)> GetAsync(Guid id, CancellationToken ct)
    {
        var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        return (conversation, ConversationConfiguration.Options(conversation, defaults));
    }

    public async Task<Conversation> UpdateAsync(Guid id, int revision, string modelName, string? systemPrompt,
        string? contextNotes, ChatOptions? options, CancellationToken ct)
    {
        var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        if (options is null) throw new ArgumentException("Réglages requis.");
        if (modelName != conversation.Model && !(await model.ListModelsAsync(ct)).Contains(modelName))
            throw new ArgumentException("Choisissez un modèle local capable de générer du texte.");
        conversation.Configure(modelName, systemPrompt, contextNotes, ConversationConfiguration.Serialize(options));
        SlidingContext.ValidatePrompt(ConversationConfiguration.System(conversation), "Bonjour", options.Context());
        await repository.SaveAsync(ct);
        return conversation;
    }
}
