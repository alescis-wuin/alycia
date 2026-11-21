using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

public sealed record ProjectChoice(Guid? Id, string Name, bool Archived = false)
{
    public override string ToString() => Archived ? Name + " (archivé)" : Name;
}
public sealed record ProjectSettings(Guid Id, string Instructions, string DefaultModel, bool Archived, int Revision, WorkspaceNoteView[] Notes);
public sealed record SourcePreview(string Name, string Sha256, string Text, int Start, int End, bool Partial);

public sealed partial class ChatApiClient
{
    public Task<ProjectChoice[]> ProjectsAsync(CancellationToken ct) => SendAsync<ProjectChoice[]>(HttpMethod.Get, "/projects", null, ct);
    public Task<ProjectChoice> NewProjectAsync(string name, CancellationToken ct) =>
        SendAsync<ProjectChoice>(HttpMethod.Post, "/projects", new { name }, ct);
    public Task<ProjectSettings> ProjectAsync(Guid project, CancellationToken ct) =>
        SendAsync<ProjectSettings>(HttpMethod.Get, $"/projects/{project}", null, ct);
    public Task<ProjectSettings> SaveProjectAsync(ProjectSettings edited, CancellationToken ct) =>
        SendAsync<ProjectSettings>(HttpMethod.Put, $"/projects/{edited.Id}", edited, ct);
    public Task<ConversationSummary> MoveAsync(ConversationSummary chat, Guid? workspaceId, CancellationToken ct) =>
        SendAsync<ConversationSummary>(HttpMethod.Put, $"/conversations/{chat.Id}/project", new { chat.Revision, workspaceId }, ct);
    public Task<ProjectSettings> SaveNoteAsync(ProjectSettings project, Guid? note, string text, Guid? originConversationId, int? originSequence, CancellationToken ct) =>
        SendAsync<ProjectSettings>(note.HasValue ? HttpMethod.Put : HttpMethod.Post, $"/projects/{project.Id}/notes" + (note.HasValue ? "/" + note.Value : ""),
            new { project.Revision, text, originConversationId, originSequence }, ct);
    public Task<ProjectSettings> DeleteNoteAsync(ProjectSettings project, Guid note, CancellationToken ct) =>
        SendAsync<ProjectSettings>(HttpMethod.Delete, $"/projects/{project.Id}/notes/{note}?revision={project.Revision}", null, ct);
    public Task<FileItem[]> ProjectFilesAsync(Guid project, CancellationToken ct) =>
        SendAsync<FileItem[]>(HttpMethod.Get, $"/projects/{project}/files", null, ct);
    public Task<FileItem> SetProjectFileModeAsync(ProjectSettings project, Guid file, string mode, CancellationToken ct) =>
        SendAsync<FileItem>(HttpMethod.Put, $"/projects/{project.Id}/files/{file}", new { project.Revision, mode }, ct);
    public Task RemoveProjectFileAsync(ProjectSettings project, Guid file, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Delete, $"/projects/{project.Id}/files/{file}?revision={project.Revision}", null, ct);
    public Task RestoreFileModeAsync(ConversationSummary chat, Guid file, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Delete, $"/conversations/{chat.Id}/overrides/{file}?revision={chat.Revision}", null, ct);
    public Task<FileItem[]> ShareFileAsync(Guid chat, Guid file, CancellationToken ct) =>
        SendAsync<FileItem[]>(HttpMethod.Post, $"/conversations/{chat}/files/{file}/share", null, ct);
    public Task<SourcePreview> SourceAsync(Guid owner, bool project, Guid file, CancellationToken ct, string? sha = null, int? from = null, int? to = null)
    {
        var route = $"/{(project ? "projects" : "conversations")}/{owner}/files/{file}/preview";
        if (sha is not null) route += "?sha=" + Uri.EscapeDataString(sha) + "&start=" + (from ?? 0) + (to.HasValue ? "&end=" + to.Value : "");
        return SendAsync<SourcePreview>(HttpMethod.Get, route, null, ct);
    }
}
