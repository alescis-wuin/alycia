namespace Alycia.Modules.Chat.Domain;

public sealed class Conversation
{
    public Guid Id { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped, System.Text.Json.Serialization.JsonIgnore]
    public ConversationProjectSnapshot? ProjectSnapshot { get; set; }

    public Conversation DetachedCopy()
    {
        var copy = (Conversation)MemberwiseClone();
        if (ProjectSnapshot is { } snapshot) copy.ProjectSnapshot = snapshot with { Notes = snapshot.Notes.ToArray() };
        return copy;
    }

    public void AssignWorkspace(Guid? id)
    {
        if (WorkspaceId == id) return;
        WorkspaceId = id;
        ProjectSnapshot = null;
        Touch();
    }

    public string Title { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public string SystemPrompt { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public int Revision { get; private set; }
    public int MessageCount { get; private set; }
    public string SettingsJson { get; private set; } = "{}";
    public string ContextNotes { get; private set; } = "";

    public string MemoryText { get; private set; } = "";
    public int MemoryThroughSequence { get; private set; }
    public string MemorySourceHash { get; private set; } = "";
    public string MemoryModel { get; private set; } = "";
    public string MemoryLedgerJson { get; private set; } = "{}";

    public void SetMemoryLedger(string json)
    {
        if (json.Length > 1_048_576) throw new ArgumentException("Mémoire structurée trop volumineuse.");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new ArgumentException("Mémoire structurée invalide.");
        MemoryLedgerJson = json; Touch();
    }

    public void SetMemory(string text, int through, string hash, string model)
    {
        if (through < 0 || through > MessageCount || through % 2 != 0 || text.Length > 131072)
            throw new ArgumentException("Mémoire de conversation invalide.");
        MemoryText = text; MemoryThroughSequence = through; MemorySourceHash = hash; MemoryModel = model;
        Touch();
    }

    private Conversation() { }

    public static Conversation Create(string? title, string? model, string? systemPrompt)
    {
        var now = DateTimeOffset.UtcNow;
        return new Conversation
        {
            Id = Guid.NewGuid(),
            Title = RequiredText(title, 100, "Le titre"),
            Model = RequiredText(model, 200, "Le modèle"),
            SystemPrompt = OptionalText(systemPrompt, 4000),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void Rename(string? title)
    {
        Title = RequiredText(title, 100, "Le titre");
        Touch();
    }

    public void CompleteTurn()
    {
        MessageCount = checked(MessageCount + 2);
        Touch();
    }

    public void AttachFiles() => Touch();

    public void Configure(string? model, string? systemPrompt, string? notes, string settingsJson)
    {
        var nextModel = RequiredText(model, 200, "Le modèle");
        if (nextModel != Model) { MemoryText = ""; MemoryThroughSequence = 0; MemorySourceHash = ""; MemoryModel = ""; }
        Model = nextModel;
        SystemPrompt = OptionalText(systemPrompt, 4000);
        ContextNotes = OptionalText(notes, 8000);
        SettingsJson = settingsJson;
        Touch();
    }

    private void Touch()
    {
        Revision = checked(Revision + 1);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static string RequiredText(string? value, int maxLength, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength || text.Contains('\0'))
        {
            throw new ArgumentException($"{label} doit contenir entre 1 et {maxLength} caractères, sans caractère nul.");
        }

        return text;
    }

    private static string OptionalText(string? value, int maxLength)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > maxLength || text.Contains('\0'))
        {
            throw new ArgumentException($"L’instruction système est limitée à {maxLength} caractères, sans caractère nul.");
        }

        return text;
    }
}

public sealed record ConversationProjectSnapshot(int Revision, string Instructions, IReadOnlyList<string> Notes)
{
    public string SystemText()
    {
        var notes = string.Join("\n", Notes);
        return (Instructions.Length == 0 ? "" : "Consignes du projet :\n" + Instructions + "\n\n")
            + (notes.Length == 0 ? "" : "Notes de référence partagées par l’utilisateur :\n" + notes + "\n\n");
    }
}
