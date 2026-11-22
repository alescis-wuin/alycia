using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;

namespace Alycia.Modules.Documents;

public static class ArchiveContents
{
    private static readonly string[] Extensions = [".zip", ".tar", ".gz", ".tgz"];
    public static bool Supports(string name) => Extensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public static async Task ReadAsync(string name, byte[] bytes, ImportLimits limits, Func<IncomingFile, Task> read, CancellationToken ct)
    {
        try
        {
            using var input = new MemoryStream(bytes, false);
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = new ZipArchive(input, ZipArchiveMode.Read);
                if (zip.Entries.Count > limits.BatchEntries) throw new ArgumentException($"Archive limitée à {limits.BatchEntries} entrées.");
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.FullName.Replace('\\', '/').EndsWith('/')) continue;
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new ArgumentException("Les liens symboliques dans les archives ne sont pas importés.");
                    if (entry.Length > limits.FileBytes) throw new ArgumentException($"Entrée d’archive supérieure à {limits.FileMiB} Mio.");
                    await using var stream = entry.Open();
                    await read(new(name + "!/" + DocumentContent.NormalizeName(entry.FullName), stream,
                        SourceModifiedAt: entry.LastWriteTime.DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                        MetadataSource: "ZIP: heure locale, fuseau non fourni"));
                }
            }
            else if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                await ReadTarAsync(input, name, limits, read, ct);
            else
            {
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                    await ReadTarAsync(gzip, name, limits, read, ct);
                else await read(new(name + "!/" + Path.GetFileNameWithoutExtension(name), gzip));
            }
        }
        catch (InvalidDataException) { throw new ArgumentException("Archive invalide, chiffrée ou non prise en charge. Aucun fichier de ce lot n’a été enregistré."); }
    }

    private static async Task ReadTarAsync(Stream stream, string name, ImportLimits limits, Func<IncomingFile, Task> read, CancellationToken ct)
    {
        using var tar = new TarReader(stream, leaveOpen: true);
        var entries = 0;
        while (tar.GetNextEntry() is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            if (++entries > limits.BatchEntries) throw new ArgumentException($"Archive limitée à {limits.BatchEntries} entrées.");
            if (entry.Length > limits.FileBytes) throw new ArgumentException($"Entrée TAR supérieure à {limits.FileMiB} Mio.");
            if (entry.EntryType == TarEntryType.Directory) continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new ArgumentException("Seuls les fichiers réguliers des archives TAR sont acceptés.");
            if (entry.DataStream is { } data)
                await read(new(name + "!/" + DocumentContent.NormalizeName(entry.Name), data,
                    SourceModifiedAt: entry.ModificationTime.ToUniversalTime().ToString("O"), MetadataSource: "TAR: UTC"));
        }
    }
}
