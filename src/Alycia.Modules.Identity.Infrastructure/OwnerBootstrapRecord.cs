namespace Alycia.Modules.Identity.Infrastructure;

// Persistent security metadata. The raw bootstrap secret is never stored.
public sealed class OwnerBootstrapRecord
{
    public short Id { get; private set; } = 1;
    public byte[] SecretHash { get; private set; } = [];
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public int IncorrectAttempts { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    private OwnerBootstrapRecord() { } // EF Core materialization.
}
