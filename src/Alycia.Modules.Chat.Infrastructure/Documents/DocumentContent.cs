using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure.Documents;

public static class DocumentContent
{
    public const int MaxTextBytes = 200_000;

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nom de fichier vide.");
        name = name.Replace('\\', '/');
        var display = string.Concat(name.Take(180).Select(c => char.IsControl(c) ? '�' : c));
        void Reject(string reason) => throw new ArgumentException($"Chemin refusé « {display} » : {reason}.");
        if (name.Length > 1024) Reject("plus de 1024 caractères");
        if (name.Any(char.IsControl)) Reject("caractère de contrôle dans le nom");
        if (name.StartsWith('/') || name.Contains(":/", StringComparison.Ordinal)
            || name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':')
            Reject("un chemin relatif est requis, sans racine, lecteur ni URI");
        if (name.EndsWith('/')) Reject("un nom de fichier est requis");
        var parts = name.Split('/');
        // ./ and repeated separators are common in ZIP/TAR exports. Never resolve ..,
        // even when it could be collapsed within a root; names are labels, bytes use GUID paths.
        if (parts.Contains("..", StringComparer.Ordinal)) Reject("la remontée de répertoire « .. » est interdite");
        var normalized = string.Join('/', parts.Where(part => part is not ("" or ".")));
        if (string.IsNullOrWhiteSpace(normalized)) Reject("nom de fichier vide après normalisation");
        return normalized;
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream input, int maximum, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > maximum) throw new ArgumentException($"Flux limité à {maximum:N0} octets.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }

    public static (string Text, string Status) Extract(string name, byte[] bytes)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".svg", ".heic", ".ico" }.Contains(ext))
            return ("", "Image conservée · analyse et OCR exclus");
        string text;
        var partial = false;
        try
        {
            if (StructuredDocuments.Supports(name))
            {
                var document = StructuredDocuments.Extract(name, bytes);
                return (document.Text, document.Text.Length == 0 ? "Aucun texte extractible · sans OCR"
                    : document.Complete ? "Texte extrait avec coordonnées" : "Texte extrait partiellement · coordonnées conservées, structure non exhaustive");
            }
            if (ext == ".odt")
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException("Contenu ODT absent.");
                if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException("Document trop volumineux.");
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
                var xml = XDocument.Load(reader);
                text = string.Join(" ", xml.DescendantNodes().OfType<XText>().Select(t => t.Value));
                partial = true;
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
