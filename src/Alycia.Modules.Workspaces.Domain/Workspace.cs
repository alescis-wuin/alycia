namespace Alycia.Modules.Workspaces.Domain;

public sealed class Workspace
{
    public const int MaxNameLength = 100;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Workspace() { } // EF Core materialization only.

    public Workspace(Guid id, string? name, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Workspace ID must not be empty.", nameof(id));
        }

        if (createdAtUtc == default || createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Creation time must be UTC.", nameof(createdAtUtc));
        }

        Id = id;
        Name = NormalizeName(name);
        CreatedAtUtc = createdAtUtc;
    }

    public static Workspace Create(string? name) =>
        new(Guid.NewGuid(), name, DateTimeOffset.UtcNow);

    private static string NormalizeName(string? name)
    {
        if (name is null)
        {
            throw new ArgumentException("Workspace name is required.", nameof(name));
        }

        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Workspace name is required.", nameof(name));
        }

        if (normalized.Length > MaxNameLength)
        {
            throw new ArgumentException("Workspace name exceeds 100 characters.", nameof(name));
        }

        return normalized;
    }
}
