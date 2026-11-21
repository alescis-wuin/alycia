using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

internal sealed record DocumentChange(int Completed, ConversationSummary? Conversation, string? Error);

internal sealed class DocumentList
{
    private readonly ChatApiClient _client;
    private readonly Guid _owner;
    private readonly ProjectSettings? _project;
    private readonly ConversationSummary? _chat;
    internal FileItem[] Files { get; }
    internal bool Archived => _project?.Archived == true;

    private DocumentList(ChatApiClient client, Guid owner, ProjectSettings? project, ConversationSummary? chat, FileItem[] files) =>
        (_client, _owner, _project, _chat, Files) = (client, owner, project, chat, files);

    internal static async Task<DocumentList> LoadAsync(ChatApiClient client, Guid owner, bool project, CancellationToken ct)
    {
        if (project)
            return new(client, owner, await client.ProjectAsync(owner, ct), null, await client.ProjectFilesAsync(owner, ct));
        return new(client, owner, null, (await client.SettingsAsync(owner, ct)).Conversation, await client.FilesAsync(owner, ct));
    }

    internal FileItem[] Filter(string? text) => Files.Where(f => f.Name.Contains(text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();

    internal bool CanRestore(FileItem[] selection) => _chat is not null && selection.Length > 0 && selection.All(f => f.WorkspaceId.HasValue);
    internal bool CanShare(FileItem[] selection) => _chat?.WorkspaceId is not null && selection.Length > 0
        && selection.All(f => f.WorkspaceId is null && f.ContextMode != "excluded");

    internal Task<SourcePreview> PreviewAsync(FileItem file, CancellationToken ct) => _client.SourceAsync(_owner, _project is not null, file.Id, ct);

    internal async Task<DocumentChange> ChangeModesAsync(FileItem[] selection, string mode, CancellationToken ct)
    {
        int revision = _project?.Revision ?? _chat!.Revision;
        int completed = 0;
        string? error = null;
        try
        {
            foreach (var file in selection)
            {
                if (_project is not null) await _client.SetProjectFileModeAsync(_project with { Revision = revision }, file.Id, mode, ct);
                else await _client.ConfigureFileAsync(_owner, file.Id, revision, mode, ct);
                revision++;
                completed++;
            }
        }
        catch (ChatApiException failure) { error = failure.Message; }
        var updated = _chat is null ? null : (await _client.SettingsAsync(_owner, ct)).Conversation;
        return new(completed, updated, error);
    }

    internal async Task<ConversationSummary> RestoreAsync(FileItem[] selection, CancellationToken ct)
    {
        var current = _chat!;
        foreach (var file in selection)
        {
            await _client.RestoreFileModeAsync(current, file.Id, ct);
            current = (await _client.SettingsAsync(_owner, ct)).Conversation;
        }
        return current;
    }

    internal async Task ShareAsync(FileItem[] selection, CancellationToken ct)
    {
        foreach (var file in selection) await _client.ShareFileAsync(_owner, file.Id, ct);
    }

    internal Task RemoveAsync(FileItem file, CancellationToken ct) => _client.RemoveProjectFileAsync(_project!, file.Id, ct);
}
