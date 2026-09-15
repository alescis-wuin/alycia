namespace Alicia.Application.Providers;

public sealed record InferenceProviderUpdateInfo
{
    public InferenceProviderUpdateInfo(
        string? installedVersion,
        string validatedVersion,
        string? latestVersion,
        bool isManagedInstallation,
        bool isUpdateAvailable,
        bool isLatestVersionValidated,
        string detail)
    {
        if (string.IsNullOrWhiteSpace(validatedVersion))
        {
            throw new ArgumentException(
                "Validated provider version cannot be empty.",
                nameof(validatedVersion));
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException(
                "Provider update detail cannot be empty.",
                nameof(detail));
        }

        string? normalizedInstalledVersion = NormalizeOptional(installedVersion);
        string? normalizedLatestVersion = NormalizeOptional(latestVersion);

        if (isManagedInstallation && normalizedInstalledVersion is null)
        {
            throw new ArgumentException(
                "A managed provider installation requires an installed version.",
                nameof(installedVersion));
        }

        if (isUpdateAvailable
            && (!isManagedInstallation || normalizedInstalledVersion is null))
        {
            throw new ArgumentException(
                "An available managed update requires an installed managed version.",
                nameof(isUpdateAvailable));
        }

        if (isLatestVersionValidated && normalizedLatestVersion is null)
        {
            throw new ArgumentException(
                "A validated latest provider version must be known.",
                nameof(latestVersion));
        }

        InstalledVersion = normalizedInstalledVersion;
        ValidatedVersion = validatedVersion.Trim();
        LatestVersion = normalizedLatestVersion;
        IsManagedInstallation = isManagedInstallation;
        IsUpdateAvailable = isUpdateAvailable;
        IsLatestVersionValidated = isLatestVersionValidated;
        Detail = detail.Trim();
    }

    public string? InstalledVersion { get; }

    public string ValidatedVersion { get; }

    public string? LatestVersion { get; }

    public bool IsManagedInstallation { get; }

    public bool IsUpdateAvailable { get; }

    public bool IsLatestVersionValidated { get; }

    public string Detail { get; }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
