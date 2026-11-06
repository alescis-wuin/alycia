namespace Alycia.Modules.Identity.Domain;

public sealed class User
{
    public const int MinLoginLength = 3;
    public const int MaxLoginLength = 64;

    public Guid Id { get; private set; }
    public string LoginIdentifier { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;

    private User() { } // EF Core materialization only.

    public User(Guid id, string? loginIdentifier)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User ID must not be empty.", nameof(id));
        }

        Id = id;
        LoginIdentifier = NormalizeLoginIdentifier(loginIdentifier);
    }

    public static User Create(string? loginIdentifier) =>
        new(Guid.NewGuid(), loginIdentifier);

    public void SetPasswordHash(string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password verifier is required.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }

    public static string NormalizeLoginIdentifier(string? loginIdentifier)
    {
        if (loginIdentifier is null)
        {
            throw new ArgumentException("Login identifier is required.", nameof(loginIdentifier));
        }

        var trimmed = loginIdentifier.Trim();
        if (trimmed.Length is < MinLoginLength or > MaxLoginLength)
        {
            throw new ArgumentException(
                "Login identifier must contain 3 to 64 ASCII characters.",
                nameof(loginIdentifier));
        }

        foreach (var character in trimmed)
        {
            var permitted = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.' or '_' or '-';

            if (!permitted)
            {
                throw new ArgumentException(
                    "Login identifier contains an unsupported character.",
                    nameof(loginIdentifier));
            }
        }

        return trimmed.ToUpperInvariant();
    }
}
