namespace Alycia.Modules.Chat.Application;

public sealed record SourceChunk(Guid FileId, string Path, string Sha256, string Symbol, string Kind,
    int Start, int End, int StartLine, int EndLine, string Text, string[] References, bool CompleteUnit, bool CompleteSource = true);
public sealed record ProjectIndexSnapshot(IReadOnlyList<SourceChunk> Chunks, int EligibleFiles,
    int IndexedFiles, int ReusedFiles, IReadOnlyList<string> Omissions);
public interface IProjectIndex
{
    Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct);
}
