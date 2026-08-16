namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppManagedStorage
{
    internal const string InstallationMetadataFileName = "installation.json";
    internal const string ReleasesDirectoryName = "releases";
    internal const string StagingDirectoryName = ".staging";
    internal const string LogsDirectoryName = "logs";
    internal const string ModelsDirectoryName = "models";

    internal static string GetOwnedChildPath(
        string runtimeDirectory,
        params string[] components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentNullException.ThrowIfNull(components);

        string root = Path.GetFullPath(runtimeDirectory);
        string candidate = Path.GetFullPath(Path.Combine(new[] { root }.Concat(components).ToArray()));
        string relative = Path.GetRelativePath(root, candidate);

        if (string.Equals(relative, ".", StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider storage path escaped the managed runtime directory.");
        }

        return candidate;
    }

    internal static long GetRuntimeBytes(string runtimeDirectory)
    {
        long total = 0;
        total = AddSaturated(
            total,
            GetFileBytes(GetOwnedChildPath(runtimeDirectory, InstallationMetadataFileName)));
        total = AddSaturated(
            total,
            GetDirectoryBytes(GetOwnedChildPath(runtimeDirectory, ReleasesDirectoryName)));
        total = AddSaturated(
            total,
            GetDirectoryBytes(GetOwnedChildPath(runtimeDirectory, StagingDirectoryName)));
        total = AddSaturated(
            total,
            GetDirectoryBytes(GetOwnedChildPath(runtimeDirectory, LogsDirectoryName)));
        return total;
    }

    internal static long GetModelCacheBytes(string runtimeDirectory)
    {
        return GetDirectoryBytes(GetOwnedChildPath(runtimeDirectory, ModelsDirectoryName));
    }

    internal static IReadOnlyList<string> GetRetainedReleaseDirectories(
        string runtimeDirectory,
        string activeRelease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeRelease);

        string releasesDirectory = GetOwnedChildPath(runtimeDirectory, ReleasesDirectoryName);

        if (!Directory.Exists(releasesDirectory) || IsReparsePoint(releasesDirectory))
        {
            return [];
        }

        List<string> retained = [];

        foreach (string directory in Directory.EnumerateDirectories(releasesDirectory))
        {
            string releaseName = Path.GetFileName(directory);

            if (string.Equals(releaseName, activeRelease, StringComparison.Ordinal)
                || !IsManagedReleaseName(releaseName))
            {
                continue;
            }

            retained.Add(Path.GetFullPath(directory));
        }

        retained.Sort(StringComparer.Ordinal);
        return retained;
    }

    internal static void DeleteRuntimeArtifacts(string runtimeDirectory)
    {
        DeleteOwnedFile(
            runtimeDirectory,
            GetOwnedChildPath(runtimeDirectory, InstallationMetadataFileName));
        DeleteOwnedDirectory(
            runtimeDirectory,
            GetOwnedChildPath(runtimeDirectory, ReleasesDirectoryName));
        DeleteOwnedDirectory(
            runtimeDirectory,
            GetOwnedChildPath(runtimeDirectory, StagingDirectoryName));
        DeleteOwnedDirectory(
            runtimeDirectory,
            GetOwnedChildPath(runtimeDirectory, LogsDirectoryName));
    }

    internal static void DeleteModelCache(string runtimeDirectory)
    {
        DeleteOwnedDirectory(
            runtimeDirectory,
            GetOwnedChildPath(runtimeDirectory, ModelsDirectoryName));
    }

    internal static void DeleteOwnedDirectory(
        string runtimeDirectory,
        string directoryPath)
    {
        string ownedPath = RequireOwnedChild(runtimeDirectory, directoryPath);
        DirectoryInfo directory = new(ownedPath);

        if (!directory.Exists)
        {
            return;
        }

        DeleteDirectoryWithoutFollowingLinks(directory);
    }

    private static void DeleteOwnedFile(
        string runtimeDirectory,
        string filePath)
    {
        string ownedPath = RequireOwnedChild(runtimeDirectory, filePath);

        if (File.Exists(ownedPath))
        {
            File.Delete(ownedPath);
        }
    }

    private static long GetDirectoryBytes(string directoryPath)
    {
        DirectoryInfo directory = new(directoryPath);

        if (!directory.Exists || IsReparsePoint(directory))
        {
            return 0;
        }

        long total = 0;

        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if (IsReparsePoint(entry))
            {
                continue;
            }

            total = entry switch
            {
                FileInfo file => AddSaturated(total, Math.Max(0, file.Length)),
                DirectoryInfo child => AddSaturated(total, GetDirectoryBytes(child.FullName)),
                _ => total,
            };
        }

        return total;
    }

    private static long GetFileBytes(string filePath)
    {
        FileInfo file = new(filePath);
        return file.Exists && !IsReparsePoint(file)
            ? Math.Max(0, file.Length)
            : 0;
    }

    private static void DeleteDirectoryWithoutFollowingLinks(DirectoryInfo directory)
    {
        if (IsReparsePoint(directory))
        {
            directory.Delete();
            return;
        }

        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if (IsReparsePoint(entry))
            {
                entry.Delete();
                continue;
            }

            if (entry is DirectoryInfo child)
            {
                DeleteDirectoryWithoutFollowingLinks(child);
            }
            else
            {
                entry.Delete();
            }
        }

        directory.Delete();
    }

    private static string RequireOwnedChild(
        string runtimeDirectory,
        string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        string root = Path.GetFullPath(runtimeDirectory);
        string candidate = Path.GetFullPath(candidatePath);
        string relative = Path.GetRelativePath(root, candidate);

        if (string.Equals(relative, ".", StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider maintenance refused to delete a path outside the managed runtime directory.");
        }

        return candidate;
    }

    private static bool IsManagedReleaseName(string releaseName)
    {
        try
        {
            _ = LlamaCppReleasePolicy.ParseReleaseSequence(releaseName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        return IsReparsePoint(new DirectoryInfo(path));
    }

    private static bool IsReparsePoint(FileSystemInfo entry)
    {
        return (entry.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static long AddSaturated(long left, long right)
    {
        return right > long.MaxValue - left
            ? long.MaxValue
            : left + right;
    }
}
