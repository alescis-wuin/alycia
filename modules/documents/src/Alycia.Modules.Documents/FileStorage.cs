namespace Alycia.Modules.Documents;

public sealed class FileStorage(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string DirectoryFor(Guid conversation) => Path.Combine(Root, conversation.ToString("N"));
    public string PathFor(Guid conversation, Guid file) => Path.Combine(DirectoryFor(conversation), file.ToString("N") + ".bin");
    public string WorkspaceDirectory(Guid id) => Path.Combine(Root, "projects", id.ToString("N"));
    public string WorkspacePath(Guid project, Guid file) => Path.Combine(WorkspaceDirectory(project), file.ToString("N") + ".bin");
    public void DeleteConversation(Guid conversation)
    {
        var directory = DirectoryFor(conversation);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
