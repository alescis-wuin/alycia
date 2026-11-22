namespace Alycia.Modules.Workspaces.Domain;

// Historical EF mapping. Runtime profile access belongs to the Workspaces adapter.
public sealed class WorkspaceProfile
{
    public Guid Id { get; set; }
    public string Instructions { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string NotesJson { get; set; } = "[]";
    public bool Archived { get; set; }
    public int Revision { get; set; }
}
