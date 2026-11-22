using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Retrieval;

public delegate IQueryBriefRefiner CreateQueryBriefRefiner(IChatModel model);
