using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Presentation.Navigation;

internal sealed class ProjectNavigation(ChatApiClient client)
{
    public static readonly ProjectChoice Outside = new(null, "Hors projet");
    public ProjectChoice Project { get; set; } = Outside;
    public ProjectChoice[] Projects { get; private set; } = [Outside];
    public ConversationSummary? Conversation { get; set; }
    public string SearchQuery { get; private set; } = "";

    public async Task RefreshProjectsAsync(Guid? selectedId, CancellationToken ct)
    {
        var choices = await client.ProjectsAsync(ct);
        Projects = [Outside, .. choices];
        Project = Projects.FirstOrDefault(p => p.Id == selectedId) ?? Outside;
    }

    public Task<ConversationSearch> SearchAsync(string query, CancellationToken ct)
    {
        SearchQuery = query;
        return client.SearchAsync(query, 0, ct, Project.Id, true);
    }

    public async Task<ConversationSummary> CreateConversationAsync(string? title, string model, string? systemPrompt, CancellationToken ct)
    {
        if (Project.Id is Guid project)
        {
            var defaults = await client.ProjectAsync(project, ct);
            if (defaults.DefaultModel.Length > 0) model = defaults.DefaultModel;
        }
        return await client.CreateAsync(string.IsNullOrWhiteSpace(title) ? "Nouvelle conversation" : title, model, systemPrompt, ct, Project.Id);
    }

    public async Task<ConversationSummary[]> SelectConversationAsync(ConversationSummary[] items, Guid? requestedId, CancellationToken ct)
    {
        var selected = items.FirstOrDefault(item => item.Id == requestedId) ?? items.FirstOrDefault();
        if (requestedId.HasValue && selected?.Id != requestedId.Value)
        {
            // A filter may hide a newly created or renamed conversation in the current project.
            var requested = (await client.SettingsAsync(requestedId.Value, ct)).Conversation;
            if (requested.WorkspaceId == Project.Id)
            {
                Conversation = requested;
                return [requested, .. items];
            }
        }
        Conversation = selected;
        return items;
    }
}
