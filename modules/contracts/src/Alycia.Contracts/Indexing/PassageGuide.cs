namespace Alycia.Contracts.Indexing;

public sealed record PassageGuide(string Reference, string Summary, string Terms, string Identity, string State,
    string? Created, string? Modified, DateTimeOffset? Uploaded, DateTimeOffset IndexedAt, string NodeKey, string Level, bool Partial = false);
