namespace Alycia.Contracts.Documents;

public sealed record IncomingFile(string Name, Stream Content, string SourceCreatedAt = "", string SourceModifiedAt = "", string MetadataSource = "unknown");
