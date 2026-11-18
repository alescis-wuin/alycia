using System.Text.Json;

namespace Alycia.Modules.Chat.Domain;

// The canonical name and identity belong to the Workspaces module.
public sealed class WorkspaceProfile
{
    public Guid Id { get; set; }
    public string Instructions { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string NotesJson { get; set; } = "[]";
    public bool Archived { get; set; }
    public int Revision { get; set; }
    public IReadOnlyList<WorkspaceNote> ReadNotes() => JsonSerializer.Deserialize<WorkspaceNote[]>(NotesJson) ?? [];

    public string SystemText()
    {
        var notes = string.Join("\n", ReadNotes().Select(n => n.Text));
        return (Instructions.Length == 0 ? "" : "Consignes du projet :\n" + Instructions + "\n\n")
            + (notes.Length == 0 ? "" : "Notes de référence partagées par l’utilisateur :\n" + notes + "\n\n");
    }
}

public sealed record WorkspaceNote(Guid Id, string Text, Guid? OriginConversationId, int? OriginSequence, DateTimeOffset UpdatedAtUtc);

public sealed class WorkspaceFileOverride
{
    public Guid ConversationId { get; set; }
    public Guid FileId { get; set; }
    public string Mode { get; set; } = "excluded";
}
