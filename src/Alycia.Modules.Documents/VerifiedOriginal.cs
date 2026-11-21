using System.Buffers;
using System.Security.Cryptography;
using Alycia.Contracts.Chat;

namespace Alycia.Modules.Documents;

public static class VerifiedOriginal
{
    // Hash and retain exactly the bytes that will be served, even if a volume is modified externally.
    // Small payloads use memory. Large payloads spill to a private delete-on-close file.
    public static async Task<(Stream Content, DateTimeOffset Modified)> OpenAsync(FileStorage storage, Guid owner,
        bool project, Guid file, long expectedSize, string sha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = project ? storage.WorkspacePath(owner, file) : storage.PathFor(owner, file);
        await using var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var modified = File.GetLastWriteTimeUtc(original.SafeFileHandle);
        if (expectedSize is < 0 or > ImportLimits.HardFileBytes || expectedSize != original.Length)
            throw new InvalidDataException("La taille de l’original a changé.");
        Stream snapshot;
        if (expectedSize <= 1024 * 1024) snapshot = new MemoryStream((int)expectedSize);
        else
        {
            var options = new FileStreamOptions
            {
                Access = FileAccess.ReadWrite, Mode = FileMode.CreateNew, Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            snapshot = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), options);
        }
        try
        {
            await CopyAsync(original, snapshot, expectedSize, sha256, ct);
            snapshot.Position = 0;
            return (snapshot, modified);
        }
        catch { await snapshot.DisposeAsync(); throw; }
    }

    public static async Task CopyAsync(Stream source, Stream destination, long expectedSize, string sha256, CancellationToken ct)
    {
        if (expectedSize is < 0 or > ImportLimits.HardFileBytes) throw new InvalidDataException("Taille d’original invalide.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            long size = 0;
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(0, 65536), ct)) != 0)
            {
                size += count;
                if (size > expectedSize) throw new InvalidDataException("La taille de l’original a changé.");
                hash.AppendData(buffer, 0, count);
                await destination.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (size != expectedSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("L’original a changé : réimportez-le avant consultation.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
