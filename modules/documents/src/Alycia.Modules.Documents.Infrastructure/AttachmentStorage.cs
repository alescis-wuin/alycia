
namespace Alycia.Modules.Documents.Infrastructure;

public static class AttachmentStorage
{
    public static string PathFor(this FileStorage storage, Attachment file) => file.WorkspaceId is Guid project
        ? storage.WorkspacePath(project, file.Id) : storage.PathFor(file.ConversationId!.Value, file.Id);
}
