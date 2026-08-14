namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppReleasePolicy
{
    public const string ValidatedReleaseTag = "b10435";

    public const string ValidatedCommitSha = "9e40df63ba151d771d8b247ac4011cf203337e99";

    public static Uri ValidatedTarballUri { get; } = new(
        $"https://api.github.com/repos/ggml-org/llama.cpp/tarball/{ValidatedCommitSha}");

    public static int CompareReleaseTags(string left, string right)
    {
        return ParseReleaseSequence(left).CompareTo(ParseReleaseSequence(right));
    }

    public static int ParseReleaseSequence(string releaseTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseTag);

        string normalized = releaseTag.Trim();

        if (normalized.Length < 2
            || normalized[0] is not ('b' or 'B')
            || !int.TryParse(
                normalized.AsSpan(1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int sequence)
            || sequence < 1)
        {
            throw new ArgumentException(
                "llama.cpp release tags must use the form b<positive build number>.",
                nameof(releaseTag));
        }

        return sequence;
    }
}
