namespace Alycia.Contracts.Documents;

public sealed record ToolConversationPolicy(string SettingsJson, int MessageCount);

public interface IToolConversationPolicy
{
    Task<ToolConversationPolicy?> ReadAsync(Guid conversation, CancellationToken ct);
    Task<bool> HasHistoryAsync(Guid conversation, CancellationToken ct);
}
