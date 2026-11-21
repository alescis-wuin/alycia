namespace Alycia.Contracts.Chat;

public sealed record HistoryEvidence(string Reference, Guid ConversationId, int Sequence, string Role,
    DateTimeOffset CreatedAtUtc, string Sha256, int Start, int End, bool Partial);
public sealed record MemoryEntry(string Key, string Kind, string Quote, int Sequence, string Role, string SourceHash,
    bool Correction = false, bool Fallback = false);
public sealed record MemoryLedger(int Version, int Through, string ChainHash, MemoryEntry[] Entries, int Omitted = 0, int FallbackSegments = 0,
    string ModelIdentity = "", string PromptVersion = "memory-v1");
public sealed record MemoryReport(int Through, int Stored, int Selected, int Omitted, int FallbackSegments, int Conflicts);
public sealed record TemporalObservation(string Key, int Sequence, string Role, string Status, DateTimeOffset Observed,
    DateTimeOffset? ValidUntil, int? CorrectedBy, int Version = 1);
public sealed record TemporalReport(int Indexed, int Total, HistoryEvidence[] Sources, string? Warning, string Perspective, TemporalObservation[]? Observations = null);
