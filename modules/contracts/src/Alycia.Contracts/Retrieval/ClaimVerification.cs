using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Retrieval;

public sealed record ClaimVerification(bool Enabled, int TimeoutSeconds, ProjectSearch? Search);

public sealed record VerificationResult(VerificationFollowup Followup, IReadOnlyList<ModelCallUsage> Calls);
