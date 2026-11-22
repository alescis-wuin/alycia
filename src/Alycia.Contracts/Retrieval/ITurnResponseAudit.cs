using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Contracts.Inference;
using Alycia.Contracts.Tools;

namespace Alycia.Contracts.Retrieval;

public sealed record ReviewedReply(string Reply, CitationReport Citations, ClaimReport Claims, VerificationFollowup? Followup);

public interface ITurnResponseAudit
{
    Task<ReviewedReply> ReviewAsync(Guid id, GenerationReply generated, ContextWindow window, ClaimVerification verification,
        IModelMeter measured, Func<ModelDelta, Task>? onDelta, CancellationToken cancellationToken);
}
