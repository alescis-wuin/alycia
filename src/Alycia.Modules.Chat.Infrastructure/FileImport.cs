using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record IncomingFile(string Name, Stream Content);
public sealed record FileSummary(Guid Id, string Name, long Size, string Status, int AnchorSequence,
    DateTimeOffset CreatedAtUtc, Guid? ArchiveId, string Sha256);

public sealed class FileStorage(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string DirectoryFor(Guid conversation) => Path.Combine(Root, conversation.ToString("N"));
    public string PathFor(Guid conversation, Guid file) => Path.Combine(DirectoryFor(conversation), file.ToString("N") + ".bin");
    public void DeleteConversation(Guid conversation)
    {
        var directory = DirectoryFor(conversation);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

public sealed class FileImport(ChatDbContext database, FileStorage storage)
{
    public const int MaxFileBytes = 25 * 1024 * 1024;
    public const int MaxBatchBytes = 100 * 1024 * 1024;
    public const int MaxEntries = 100;
    public const int MaxTextBytes = 200_000;

    public static FileSummary Summary(Attachment f) => new(f.Id, f.Name, f.Size, f.Status,
        f.AnchorSequence, f.CreatedAtUtc, f.ArchiveId, f.Sha256);

    public async Task<IReadOnlyList<FileSummary>> ImportAsync(Guid conversationId, Guid batchId,
        IReadOnlyList<IncomingFile> incoming, CancellationToken ct)
    {
        if (batchId == Guid.Empty || incoming.Count is < 1 or > MaxEntries)
            throw new ArgumentException("Import limité à 100 fichiers ; identifiant de lot requis.");
        var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new ConversationNotFoundException();
        var previous = await database.Attachments.AsNoTracking()
            .Where(f => f.ConversationId == conversationId && f.BatchId == batchId).ToListAsync(ct);
        var files = new List<Attachment>();
        var payloads = new List<(Attachment File, byte[] Bytes)>();
        long total = 0;
        async Task AddAsync(string name, Stream content, Guid? archive, bool expand)
        {
            ct.ThrowIfCancellationRequested();
            if (files.Count >= MaxEntries) throw new ArgumentException("Archive ou lot limité à 100 entrées, original compris.");
            name = NormalizeName(name);
            var bytes = await ReadBoundedAsync(content, MaxFileBytes, ct);
            total += bytes.Length;
            if (total > MaxBatchBytes) throw new ArgumentException("Lot décompressé limité à 100 Mio.");
            var file = new Attachment
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                BatchId = batchId,
                ArchiveId = archive,
                Name = name,
                Size = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                AnchorSequence = conversation.MessageCount + 1,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var isArchive = IsArchive(name);
            (file.Text, file.Status) = isArchive ? ("", expand ? "Archive importée" : "Archive imbriquée conservée sans extraction") : Extract(name, bytes);
            files.Add(file);
            payloads.Add((file, bytes));
            if (!expand || !isArchive) return;
            try
            {
                using var input = new MemoryStream(bytes, false);
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = new ZipArchive(input, ZipArchiveMode.Read);
                    if (zip.Entries.Count > MaxEntries) throw new ArgumentException("Archive limitée à 100 entrées.");
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith('/')) continue;
                        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                            throw new ArgumentException("Les liens symboliques dans les archives ne sont pas importés.");
                        if (entry.Length > MaxFileBytes) throw new ArgumentException("Entrée d’archive supérieure à 25 Mio.");
                        await using var stream = entry.Open();
                        await AddAsync(name + "!/" + NormalizeName(entry.FullName), stream, file.Id, false);
                    }
                }
                else if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                    await ReadTarAsync(input, name, file.Id);
                else
                {
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                        await ReadTarAsync(gzip, name, file.Id);
                    else await AddAsync(name + "!/" + Path.GetFileNameWithoutExtension(name), gzip, file.Id, false);
                }
            }
            catch (InvalidDataException) { throw new ArgumentException("Archive invalide, chiffrée ou non prise en charge. Aucun fichier de ce lot n’a été enregistré."); }
        }
        async Task ReadTarAsync(Stream stream, string name, Guid archive)
        {
            using var tar = new TarReader(stream, leaveOpen: true);
            var entries = 0;
            while (tar.GetNextEntry() is { } entry)
            {
                ct.ThrowIfCancellationRequested();
                if (++entries > MaxEntries) throw new ArgumentException("Archive limitée à 100 entrées.");
                if (entry.Length > MaxFileBytes) throw new ArgumentException("Entrée TAR supérieure à 25 Mio.");
                if (entry.EntryType == TarEntryType.Directory) continue;
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new ArgumentException("Seuls les fichiers réguliers des archives TAR sont acceptés.");
                if (entry.DataStream is { } data) await AddAsync(name + "!/" + NormalizeName(entry.Name), data, archive, false);
            }
        }
        foreach (var file in incoming) await AddAsync(file.Name, file.Content, null, true);
        if (previous.Count > 0)
        {
            var oldRoots = previous.Where(f => f.ArchiveId == null).Select(f => (f.Name, f.Sha256)).Order().ToArray();
            var newRoots = files.Where(f => f.ArchiveId == null).Select(f => (f.Name, f.Sha256)).Order().ToArray();
            if (!oldRoots.SequenceEqual(newRoots)) throw new ChatConflictException();
            return previous.OrderBy(f => f.CreatedAtUtc).Select(Summary).ToArray();
        }
        var stored = await database.Attachments.Where(f => f.ConversationId == conversationId)
            .Select(f => f.Size).ToListAsync(ct);
        if (stored.Count + files.Count > 500 || stored.Sum() + total > 1024L * 1024 * 1024)
            throw new ArgumentException("Limite de conversation : 500 fichiers ou 1 Gio.");
        Directory.CreateDirectory(storage.DirectoryFor(conversationId));
        var written = new List<string>();
        var committing = false;
        try
        {
            foreach (var (file, bytes) in payloads)
            {
                var path = storage.PathFor(conversationId, file.Id);
                written.Add(path);
                await File.WriteAllBytesAsync(path, bytes, ct);
            }
            database.Attachments.AddRange(files);
            conversation.AttachFiles();
            committing = true;
            await new EfChatRepository(database).SaveAsync(ct);
        }
        catch
        {
            // On ambiguous DB commit failure keep bytes: never delete a file that
            // might already be referenced. Orphans can be reconciled offline.
            if (!committing) foreach (var path in written) File.Delete(path);
            throw;
        }
        return files.Select(Summary).ToArray();
    }

    public static string NormalizeName(string name)
    {
        name = name.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || name.StartsWith('/')
            || name.Any(char.IsControl) || name.Contains(':')
            || name.Split('/').Any(part => part is "" or "." or ".."))
            throw new ArgumentException("Nom de fichier ou chemin relatif invalide.");
        return name;
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream input, int maximum, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > maximum) throw new ArgumentException("Fichier limité à 25 Mio.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }

    private static bool IsArchive(string name) => new[] { ".zip", ".tar", ".gz", ".tgz" }
        .Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public static (string Text, string Status) Extract(string name, byte[] bytes)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".svg", ".heic", ".ico" }.Contains(ext))
            return ("", "Image conservée · analyse et OCR exclus");
        string text;
        var partial = false;
        try
        {
            if (ext == ".pdf")
            {
                using var pdf = PdfDocument.Open(bytes);
                var builder = new StringBuilder();
                foreach (var page in pdf.GetPages().Take(200))
                {
                    builder.AppendLine(page.Text);
                    if (builder.Length > MaxTextBytes) break;
                }
                text = builder.ToString();
                // Page count is also bounded; mark excerpts rather than claiming completeness.
                partial = pdf.NumberOfPages > 200;
            }
            else if (ext is ".docx" or ".odt" or ".pptx" or ".xlsx")
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var builder = new StringBuilder();
                var sharedStrings = Array.Empty<string>();
                long expanded = 0;
                var entries = zip.Entries.Where(e => e.FullName == "word/document.xml" || e.FullName == "content.xml"
                    || e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml")
                    || e.FullName == "xl/sharedStrings.xml" || e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"))
                    .OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
                partial = entries.Length > 100;
                foreach (var entry in entries.Take(100))
                {
                    expanded += entry.Length;
                    if (entry.Length > 4 * 1024 * 1024 || expanded > 16 * 1024 * 1024) return ("", "Document trop volumineux pour l’extraction");
                    using var stream = entry.Open();
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
                    var xml = XDocument.Load(reader);
                    if (ext == ".xlsx")
                    {
                        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                        if (entry.FullName == "xl/sharedStrings.xml")
                        {
                            sharedStrings = xml.Descendants(ns + "si")
                                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToArray();
                            continue;
                        }
                        builder.AppendLine(entry.FullName);
                        foreach (var cell in xml.Descendants(ns + "c"))
                        {
                            var value = cell.Element(ns + "v")?.Value ?? "";
                            if ((string?)cell.Attribute("t") == "s")
                                value = int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Length
                                    ? sharedStrings[index] : "[référence indisponible]";
                            else if ((string?)cell.Attribute("t") == "inlineStr")
                                value = string.Concat(cell.Descendants(ns + "t").Select(t => t.Value));
                            builder.Append(cell.Attribute("r")?.Value).Append(": ").AppendLine(value);
                            if (builder.Length > MaxTextBytes) break;
                        }
                        if (builder.Length > MaxTextBytes) break;
                        continue;
                    }
                    foreach (var node in xml.DescendantNodes().OfType<XText>())
                    {
                        builder.Append(node.Value).Append(' ');
                        if (builder.Length > MaxTextBytes) break;
                    }
                    if (builder.Length > MaxTextBytes) break;
                }
                text = builder.ToString();
            }
            else
            {
                if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                else text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                    return ("", "Format binaire conservé · extraction non disponible");
            }
            text = text.Replace("\0", "").Trim();
            if (text.Length == 0) return ("", "Aucun texte extractible · sans OCR");
            var truncated = partial || Encoding.UTF8.GetByteCount(text) > MaxTextBytes;
            return (FileContext.Utf8Prefix(text, MaxTextBytes), truncated ? "Texte extrait partiellement" : "Texte extrait");
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return ("", "Fichier conservé · extraction non disponible");
        }
    }
}
