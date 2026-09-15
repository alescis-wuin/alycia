using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Providers;

public sealed class InferenceProviderUpdateInfoTests
{
    [Fact]
    public void UpdateInfoNormalizesProviderVersionsAndDetail()
    {
        InferenceProviderUpdateInfo info = new(
            installedVersion: " b10434 ",
            validatedVersion: " b10435 ",
            latestVersion: " b10435 ",
            isManagedInstallation: true,
            isUpdateAvailable: true,
            isLatestVersionValidated: true,
            detail: " Validated update available. ");

        Assert.Equal("b10434", info.InstalledVersion);
        Assert.Equal("b10435", info.ValidatedVersion);
        Assert.Equal("b10435", info.LatestVersion);
        Assert.True(info.IsManagedInstallation);
        Assert.True(info.IsUpdateAvailable);
        Assert.True(info.IsLatestVersionValidated);
        Assert.Equal("Validated update available.", info.Detail);
    }

    [Fact]
    public void UpdateInfoRejectsUpdateWithoutManagedInstalledVersion()
    {
        Assert.Throws<ArgumentException>(() => new InferenceProviderUpdateInfo(
            installedVersion: null,
            validatedVersion: "b10435",
            latestVersion: "b10435",
            isManagedInstallation: false,
            isUpdateAvailable: true,
            isLatestVersionValidated: true,
            detail: "Invalid."));
    }

    [Fact]
    public void UpdateResultRequiresSnapshotAndVersionInfo()
    {
        InferenceProviderSnapshot snapshot = new(
            "Provider",
            InferenceProviderState.Ready,
            version: "10435");
        InferenceProviderUpdateInfo info = new(
            installedVersion: "b10435",
            validatedVersion: "b10435",
            latestVersion: "b10435",
            isManagedInstallation: true,
            isUpdateAvailable: false,
            isLatestVersionValidated: true,
            detail: "Up to date.");

        InferenceProviderUpdateResult result = new(snapshot, info);

        Assert.Same(snapshot, result.Snapshot);
        Assert.Same(info, result.UpdateInfo);
        Assert.Throws<ArgumentNullException>(() => new InferenceProviderUpdateResult(null!, info));
        Assert.Throws<ArgumentNullException>(() => new InferenceProviderUpdateResult(snapshot, null!));
    }
}
