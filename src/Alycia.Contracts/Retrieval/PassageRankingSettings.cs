using System.Collections.Frozen;
using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Retrieval;

public sealed record PassageRankingSettings(bool FacetPlanningEnabled, bool ClassificationEnabled, int ClassificationBudget,
    int NeutralCandidates, double RelevantThreshold, double NeutralThreshold, bool RerankEnabled,
    int RerankCandidateLimit, int RerankTimeoutMs, FrozenSet<Guid>? PinnedSourceIds)
{
    public static PassageRankingSettings Capture(ChatSettings settings, IReadOnlySet<Guid>? pinned) =>
        new(settings.FacetPlanningEnabled, settings.ClassificationEnabled, settings.ClassificationBudget,
            settings.NeutralCandidates, settings.RelevantThreshold, settings.NeutralThreshold, settings.RerankEnabled,
            settings.RerankCandidateLimit, settings.RerankTimeoutMs, pinned?.ToFrozenSet());
}
