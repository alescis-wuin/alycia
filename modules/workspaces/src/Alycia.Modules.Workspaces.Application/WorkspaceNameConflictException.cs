namespace Alycia.Modules.Workspaces.Application;

public sealed class WorkspaceNameConflictException : Exception
{
    public WorkspaceNameConflictException()
        : base("A workspace with this name already exists.")
    {
    }
}
