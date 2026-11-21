using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Retrieval;

public sealed record ProjectExcerpts(string Text, IReadOnlyList<SourceExcerpt> Sources, int Available, EvidenceAssembly? Assembly = null);
