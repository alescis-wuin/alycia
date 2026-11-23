using System.Collections.Concurrent;

namespace Alycia.Modules.Inference;

public sealed class ModelTokenizationCache
{
    internal ConcurrentDictionary<string, ModelTokenization> Entries { get; } = new();
}
