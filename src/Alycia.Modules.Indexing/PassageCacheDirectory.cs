namespace Alycia.Modules.Indexing;

public sealed class PassageCacheDirectory(string root)
{
    private readonly string _root = Path.GetFullPath(root);
    public string DirectoryFor(Guid conversation) => Path.Combine(_root, conversation.ToString("N"));
}
