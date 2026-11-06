namespace Alycia.Modules.Chat.Domain;

public sealed class Conversation
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public string SystemPrompt { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public int Revision { get; private set; }
    public int MessageCount { get; private set; }

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
