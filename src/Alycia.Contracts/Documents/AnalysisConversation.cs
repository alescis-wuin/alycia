using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Documents;

public sealed record AnalysisConversation(Guid Id, string Model, string SettingsJson, int Revision);
public interface IAnalysisConversation
{
    Task<AnalysisConversation> RequireAsync(Guid owner, int? revision, CancellationToken ct);
    Task<ConversationSummary> StageAdvanceAsync(Guid owner, int revision, CancellationToken ct);
}
