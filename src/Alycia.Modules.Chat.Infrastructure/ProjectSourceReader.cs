using System.Text;
using System.Security.Cryptography;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record ProjectSource(string Text, bool Complete, string Origin);

// Only storage IDs can address originals; model-supplied paths never reach the filesystem.
public sealed class ProjectSourceReader(FileStorage storage)
{
    internal FileStorage Storage => storage;
    private static readonly HashSet<string> ExtractedFormats = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".docx", ".odt", ".pptx", ".xlsx" };
    public async Task<ProjectSource> ReadAsync(Attachment file, CancellationToken ct)
    {
        if (file.ContextMode == "excluded") throw new ArgumentException("Ce fichier est exclu de l’accès au modèle.");
        if (file.Text.Length == 0) return new("", false, file.Status);
        try
        {
            await using var stream = File.OpenRead(storage.PathFor(file));
            var bytes = await FileImport.ReadBoundedAsync(stream, FileImport.MaxFileBytes, ct);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("L’original a changé : réimportez-le avant consultation.");
            if (StructuredDocuments.Supports(file.Name))
            {
                var extracted = StructuredDocuments.Extract(file.Name, bytes);
                return new(extracted.Text, extracted.Complete, "extraction structurée v" + StructuredDocuments.Version + " ; coordonnées dans les titres, sans OCR");
            }
            if (ExtractedFormats.Contains(Path.GetExtension(file.Name)))
                return new(file.Text, false, "extraction documentaire historique partielle : " + file.Status);

            var text = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })
                ? new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2)
                : bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })
                    ? new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2)
                    : new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                throw new ArgumentException("Contenu binaire ou caractères de contrôle non pris en charge.");
            return new(text, true, "original textuel intégral");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new ArgumentException("Original indisponible ; réimportez le fichier."); }
        catch (DecoderFallbackException) { throw new ArgumentException("Original UTF-8/UTF-16 invalide."); }
    }
}
