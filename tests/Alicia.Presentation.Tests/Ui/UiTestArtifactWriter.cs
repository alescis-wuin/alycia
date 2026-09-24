using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace Alicia.Presentation.Tests.Ui;

internal static class UiTestArtifactWriter
{
    private const string CaptureEnvironmentVariable = "ALYCIA_UI_CAPTURE";
    private const string ArtifactDirectoryEnvironmentVariable = "ALYCIA_UI_ARTIFACTS_DIR";

    internal static void Capture(Window window, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (!string.Equals(
                Environment.GetEnvironmentVariable(CaptureEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string root = Environment.GetEnvironmentVariable(ArtifactDirectoryEnvironmentVariable)
            ?? Path.Combine(FindRepositoryRoot(), "artifacts", "ui-tests");
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootWithSeparator = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(rootWithSeparator, pathComparison))
        {
            throw new InvalidOperationException("UI artifact path escaped the configured artifact directory.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Avalonia Headless did not return a rendered frame.");
        frame.Save(path, PngBitmapEncoderOptions.Default);

        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(path + ".sha256", digest + "  " + Path.GetFileName(path) + Environment.NewLine);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alicia.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the Alicia repository root for UI artifacts.");
    }
}
