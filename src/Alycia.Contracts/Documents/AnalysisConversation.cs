using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;

namespace Alycia.Contracts.Documents;

public sealed record AnalysisConversation(Guid Id, string Model, int Revision);
// Legacy checkpoints hash the full serialized settings after provider window inspection.
public sealed class AnalysisCacheProfile(string serializedSettings)
{
    public string Key(string prefix) => MemoryTransfer.Hash(prefix + "|" + serializedSettings);
}

public sealed record AnalysisPreferences(SummaryParameters Summary, int Workers, AnalysisCacheProfile Cache);

public interface IAnalysisConversation
{
    Task<AnalysisConversation> RequireAsync(Guid owner, int? revision, CancellationToken ct);
    Task<AnalysisPreferences> PreferencesAsync(Guid owner, int revision, CancellationToken ct);
    Task<ConversationSummary> StageAdvanceAsync(Guid owner, int revision, CancellationToken ct);
}
