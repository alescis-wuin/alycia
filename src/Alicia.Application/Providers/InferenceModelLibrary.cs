using System.Collections.ObjectModel;

namespace Alicia.Application.Providers;

public sealed class InferenceModelLibrary
{
    private readonly ReadOnlyCollection<InferenceModelLibraryEntry> _entries;

    public InferenceModelLibrary(IEnumerable<InferenceModelLibraryEntry>? entries = null)
    {
        InferenceModelLibraryEntry[] snapshot = entries?.ToArray() ?? [];
        ValidateEntries(snapshot);
        _entries = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<InferenceModelLibraryEntry> Entries => _entries;

    public static InferenceModelLibrary Empty { get; } = new();

    public InferenceModelLibraryEntry? Find(string providerId, string modelReference)
    {
        string normalizedProviderId = NormalizeRequired(providerId, nameof(providerId));
        string normalizedModelReference = NormalizeRequired(modelReference, nameof(modelReference));

        foreach (InferenceModelLibraryEntry entry in _entries)
        {
            if (string.Equals(entry.ProviderId, normalizedProviderId, StringComparison.Ordinal)
                && string.Equals(entry.ModelReference, normalizedModelReference, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    public InferenceModelLibrary WithEntry(InferenceModelLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        List<InferenceModelLibraryEntry> updatedEntries = new(_entries.Count + 1);
        bool replaced = false;

        foreach (InferenceModelLibraryEntry existing in _entries)
        {
            if (HasSameIdentity(existing, entry))
            {
                updatedEntries.Add(entry);
                replaced = true;
            }
            else
            {
                updatedEntries.Add(existing);
            }
        }

        if (!replaced)
        {
            updatedEntries.Add(entry);
        }

        return new InferenceModelLibrary(updatedEntries);
    }

    public InferenceModelLibrary WithoutEntry(string providerId, string modelReference)
    {
        InferenceModelLibraryEntry? existing = Find(providerId, modelReference);
        if (existing is null)
        {
            return this;
        }

        return new InferenceModelLibrary(_entries.Where(entry => !HasSameIdentity(entry, existing)));
    }

    private static void ValidateEntries(IReadOnlyList<InferenceModelLibraryEntry> entries)
    {
        HashSet<string> identities = new(StringComparer.Ordinal);

        foreach (InferenceModelLibraryEntry entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            string identity = BuildIdentity(entry.ProviderId, entry.ModelReference);
            if (!identities.Add(identity))
            {
                throw new ArgumentException(
                    $"The model library contains duplicate entry '{entry.ProviderId} • {entry.ModelReference}'.",
                    nameof(entries));
            }
        }
    }

    private static bool HasSameIdentity(
        InferenceModelLibraryEntry left,
        InferenceModelLibraryEntry right)
    {
        return string.Equals(left.ProviderId, right.ProviderId, StringComparison.Ordinal)
            && string.Equals(left.ModelReference, right.ModelReference, StringComparison.Ordinal);
    }

    private static string BuildIdentity(string providerId, string modelReference)
    {
        return providerId + "\n" + modelReference;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
