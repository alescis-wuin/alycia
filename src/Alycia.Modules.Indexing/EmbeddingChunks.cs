namespace Alycia.Modules.Indexing;

public static class EmbeddingChunks
{
    public static string Text(SourceChunk c) => c.Path + "\n" + c.Symbol + "\n" + (c.IndexContext is null ? "" : c.IndexContext + "\n") + c.Text;
    // Coordinates, content and extractor versions invalidate vectors without destroying originals.
    public static string Key(SourceChunk c) => MemoryTransfer.Hash($"embeddings-v3|{c.FileId}|{c.Sha256}|{c.Start}|{c.End}|" + Text(c));

    public static IEnumerable<SourceChunk> Split(SourceChunk source, int size)
    {
        if (size is < 128 or > 3200) throw new ArgumentOutOfRangeException(nameof(size));
        for (var at = 0; at < source.Text.Length;)
        {
            var end = Math.Min(source.Text.Length, at + size);
            if (end < source.Text.Length && char.IsLowSurrogate(source.Text[end])) end--;
            var text = source.Text[at..end];
            var line = source.StartLine + source.Text.AsSpan(0, at).Count('\n');
            yield return source with
            {
                Start = source.Start + at,
                End = source.Start + end,
                Text = text,
                StartLine = line,
                EndLine = line + text.AsSpan(0, Math.Max(0, text.Length - 1)).Count('\n'),
                CompleteUnit = source.CompleteUnit && at == 0 && end == source.Text.Length
            };
            at = end;
        }
    }
}
