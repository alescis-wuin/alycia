using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed class ConversationCreation(ChatService service, IProjectModelDefaults projects)
{
    public async Task<Conversation> CreateAsync(string? title, string? model, string? systemPrompt, Guid? workspace, CancellationToken ct)
    {
        var defaultModel = workspace is Guid project ? await projects.ActiveDefaultModelAsync(project, ct) : null;
        return await service.CreateAsync(title, string.IsNullOrWhiteSpace(model) ? defaultModel : model, systemPrompt, ct, workspace);
    }
}
