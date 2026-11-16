using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record InventoryFile(Guid Id, Guid BatchId, Guid? ArchiveId, string Path, long Bytes,
    string Sha256, string Extension, string SourceCreatedAt, string SourceModifiedAt, string MetadataSource,
    DateTimeOffset ImportedAtUtc, string ExtractionStatus, string ContextMode);

public static class ProjectInventory
{
    public static InventoryFile[] Describe(IEnumerable<Attachment> files) => files
        .OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Id)
        .Select(f => new InventoryFile(f.Id, f.BatchId, f.ArchiveId, f.Name, f.Size, f.Sha256,
            Path.GetExtension(f.Name), f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource,
            f.CreatedAtUtc, f.Status, f.ContextMode)).ToArray();

    public static string Fingerprint(IEnumerable<InventoryFile> files) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(files.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Id))));

    public static string Metadata(InventoryFile file) => $"{file.Path} | {file.Bytes} octets | extension={Show(file.Extension)}"
        + $" | SHA-256={file.Sha256} | création={Show(file.SourceCreatedAt)} | modification={Show(file.SourceModifiedAt)}"
        + $" | provenance={file.MetadataSource} | import={file.ImportedAtUtc:O} | contexte={file.ContextMode}"
        + $" | extraction={file.ExtractionStatus} | id={file.Id}";

    public static string Tree(IEnumerable<InventoryFile> files)
    {
        var root = new Node();
        foreach (var file in files)
        {
            var parts = file.Path.Split('/');
            var node = root;
            foreach (var part in parts[..^1])
            {
                if (!node.Children.TryGetValue(part, out var next)) node.Children[part] = next = new();
                node = next;
            }
            node.Files.Add(file);
        }
        var text = new StringBuilder("tree.txt — périmètre importé\n")
            .AppendLine("Dates absentes : indisponibles. ZIP : heure locale sans fuseau. SHA-256 calculé sur les octets importés.")
            .AppendLine("Les dossiers exclus avant import et dossiers vides ne sont pas inventoriés. !/ marque le contenu d’une archive.")
            .AppendLine("./");
        Write(root, "", text);
        return text.ToString();
    }

    private static void Write(Node node, string prefix, StringBuilder text)
    {
        var entries = node.Children.Select(pair => (Name: pair.Key, Child: (Node?)pair.Value, File: (InventoryFile?)null))
            .Concat(node.Files.Select(file => (Name: Path.GetFileName(file.Path), Child: (Node?)null, File: (InventoryFile?)file)))
            .OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.File?.Id).ToArray();
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i]; var last = i == entries.Length - 1;
            text.Append(prefix).Append(last ? "└── " : "├── ").Append(entry.Name);
            if (entry.Child is not null)
            {
                text.AppendLine("/"); Write(entry.Child, prefix + (last ? "    " : "│   "), text);
            }
            else text.Append(" [").Append(Metadata(entry.File!)).AppendLine("]");
        }
    }

    private static string Show(string value) => value.Length == 0 ? "indisponible" : value;
    private sealed class Node
    {
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        public List<InventoryFile> Files { get; } = [];
    }
}
