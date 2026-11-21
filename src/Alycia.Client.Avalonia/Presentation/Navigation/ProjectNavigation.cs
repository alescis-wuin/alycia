using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Presentation.Navigation;

internal sealed class ProjectNavigation(ChatApiClient client)
{
    public static readonly ProjectChoice Outside = new(null, "Hors projet");
    private ProjectChoice _project = Outside;
    private ConversationSummary? _conversation;
    private long _version;
    public event Action? OwnerChanged;

    public ProjectChoice Project
    {
        get => _project;
        set
        {
            var changed = _project.Id != value.Id;
            _project = value;
            if (!changed) return;
            _conversation = null;
            ChangeOwner();
        }
    }

    public ConversationSummary? Conversation
    {
        get => _conversation;
        set
        {
            var changed = _conversation?.Id != value?.Id;
            _conversation = value;
            if (changed) ChangeOwner();
        }
    }

    public ProjectChoice[] Projects { get; private set; } = [Outside];
    public string SearchQuery { get; private set; } = "";

    private void ChangeOwner()
    {
        ++_version;
        OwnerChanged?.Invoke();
    }

    // UI continuations run on the dispatcher. Revisiting the same Id starts a new visit.
    private async Task<T?> ReadForVisitAsync<T>(long version, Task<T> request) where T : class
    {
        try
        {
            var result = await request;
            return version == _version ? result : null;
        }
        catch (ChatApiException) when (version != _version) { return null; }
    }

    public async Task RefreshProjectsAsync(Guid? selectedId, CancellationToken ct)
    {
        var version = ++_version;
        var choices = await ReadForVisitAsync(version, client.ProjectsAsync(ct));
        if (choices is null) return;
        Projects = [Outside, .. choices];
        Project = Projects.FirstOrDefault(p => p.Id == selectedId) ?? Outside;
    }

    public async Task<ConversationSearch?> SearchAsync(string query, Guid? requestedId, CancellationToken ct)
    {
        var version = ++_version;
        var page = await ReadForVisitAsync(version, client.SearchAsync(query, 0, ct, Project.Id, true));
        if (page is null) return null;
        var selected = page.Items.FirstOrDefault(item => item.Id == requestedId) ?? page.Items.FirstOrDefault();
        if (requestedId is Guid id && selected?.Id != id)
        {
            // A filter may hide a newly created or renamed conversation in the current project.
            var reply = await ReadForVisitAsync(version, client.SettingsAsync(id, ct));
            if (reply is null) return null;
            var requested = reply.Conversation;
            if (requested.WorkspaceId == Project.Id)
            {
                selected = requested;
                page = page with { Items = [requested, .. page.Items] };
            }
        }
        SearchQuery = query;
        Conversation = selected;
        return page;
    }

    public async Task<MessagePage?> ReadMessagesAsync(int? before, CancellationToken ct)
    {
        if (Conversation is not { } owner) return null;
        var version = _version;
        var page = await ReadForVisitAsync(version, client.MessagesAsync(owner.Id, before, ct));
        if (page is null) return null;
        if (page.Conversation.Id != owner.Id) throw new ChatApiException("La page reçue appartient à une autre conversation.");
        return page;
    }

    public async Task<ConversationSummary> CreateConversationAsync(string? title, string model, string? systemPrompt, CancellationToken ct)
    {
        var owner = Project.Id;
        if (owner is Guid project)
        {
            var defaults = await client.ProjectAsync(project, ct);
            if (defaults.DefaultModel.Length > 0) model = defaults.DefaultModel;
        }
        return await client.CreateAsync(string.IsNullOrWhiteSpace(title) ? "Nouvelle conversation" : title, model, systemPrompt, ct, owner);
    }
}
