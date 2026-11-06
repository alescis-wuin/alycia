using Alycia.Modules.Identity.Application;

namespace Alycia.Modules.Identity.Infrastructure;

public static class LocalBlockedOwnerPasswords
{
    public static OwnerPasswordPolicy CreatePolicy()
    {
        using var stream = typeof(LocalBlockedOwnerPasswords).Assembly
            .GetManifestResourceStream("Alycia.Identity.BlockedOwnerPasswords")
            ?? throw new InvalidOperationException("Local password list is missing.");
        using var reader = new StreamReader(stream);

        var passwords = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            // Comment lines are documentation, not blocked passwords.
            if (line.StartsWith('#') || line.Length == 0)
            {
                continue;
            }

            passwords.Add(line);
        }

        if (passwords.Count == 0)
        {
            throw new InvalidOperationException("Local password list is empty.");
        }

        return new OwnerPasswordPolicy(passwords);
    }
}
