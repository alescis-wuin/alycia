using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure.Documents;
using UglyToad.PdfPig;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record DocumentSegment(string Location, string Text);
public sealed record ExtractedDocument(string Text, bool Complete, IReadOnlyList<DocumentSegment> Segments);

// Coordinates identify original pages/parts/cells. No OCR, macros or formula evaluation.
public static class StructuredDocuments
{
    public const int Version = 1;
    public static bool Supports(string name) => new[] { ".pdf", ".docx", ".xlsx", ".pptx" }.Contains(Path.GetExtension(name).ToLowerInvariant());
    public static ExtractedDocument Extract(string name, byte[] bytes)
    {
        var segments = new List<DocumentSegment>(); var builder = new StringBuilder(); var complete = true;
        var remaining = DocumentContent.MaxTextBytes;
        bool Add(string location, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var heading = "# " + location + "\n";
            var room = remaining - Encoding.UTF8.GetByteCount(heading) - 1;
            if (room < 32) { complete = false; return false; }
            var value = FileContext.Utf8Prefix(text, room);
            if (value.Length != text.Length) complete = false;
            segments.Add(new(location, value)); builder.Append(heading).Append(value).Append('\n');
            remaining -= Encoding.UTF8.GetByteCount(heading + value + "\n");
            return complete;
        }
        if (Path.GetExtension(name).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var pdf = PdfDocument.Open(bytes);
            var read = 0;
            foreach (var page in pdf.GetPages().Take(200))
            {
                read++;
                if (string.IsNullOrWhiteSpace(page.Text)) complete = false;
                // A blank page does not prevent reading subsequent pages.
                var wasPartial = !complete; complete = true;
                var fits = Add("page:" + read, page.Text);
                complete &= !wasPartial;
                if (!fits) break;
            }
            complete &= read == pdf.NumberOfPages;
            return new(builder.ToString(), complete, segments);
        }
        using var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
        long expanded = 0;
        XDocument Xml(string path)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Partie Office absente.");
            expanded += entry.Length;
            if (entry.Length > 4 * 1024 * 1024 || expanded > 16 * 1024 * 1024) throw new InvalidDataException("Expansion Office trop volumineuse.");
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
            return XDocument.Load(reader);
        }
        string Text(XElement element) => string.Concat(element.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".docx")
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var xml = Xml("word/document.xml");
            var body = xml.Root?.Element(w + "body");
            if (body is null)
            {
                Add("word/document.xml:fallback", string.Join(" ", xml.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)));
                return new(builder.ToString(), false, segments);
            }
            var block = 0;
            foreach (var node in body.Elements())
            {
                if (node.Name == w + "sectPr") continue;
                var location = "word/document.xml:block:" + ++block;
                if (node.Name == w + "tbl")
                {
                    var row = 0;
                    foreach (var tr in node.Elements(w + "tr"))
                        if (!Add(location + ":row:" + ++row, string.Join(" | ", tr.Elements(w + "tc").Select(Text)))) break;
                }
                else if (!Add(location + ":" + (node.Descendants(w + "pStyle").FirstOrDefault()?.Attribute(w + "val")?.Value ?? "paragraph"), Text(node))) break;
                if (!complete) break;
            }
            // Headers, footnotes, drawings and layout are not reconstructed.
            complete = false;
        }
        else if (extension == ".pptx")
        {
            var parts = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"))
                .OrderBy(e => int.Parse(Regex.Match(e.FullName, @"(\d+)\.xml$").Groups[1].Value)).ToArray();
            foreach (var part in parts.Take(100))
                if (!Add(part.FullName, string.Join("\n", Xml(part.FullName).Descendants().Where(e => e.Name.LocalName == "p").Select(Text)))) break;
            // XML part coordinates remain stable; presentation order and speaker notes are not inferred.
            complete = false;
        }
        else if (extension == ".xlsx")
        {
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Xml("xl/sharedStrings.xml").Descendants(s + "si").Select(Text).ToArray();
            var sheets = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^xl/worksheets/sheet\d+\.xml$"))
                .OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
            foreach (var sheet in sheets.Take(100))
            {
                foreach (var cell in Xml(sheet.FullName).Descendants(s + "c"))
                {
                    var value = cell.Element(s + "v")?.Value;
                    if ((string?)cell.Attribute("t") == "s") value = int.TryParse(value, out var i) && i >= 0 && i < shared.Length ? shared[i] : "[chaîne indisponible]";
                    if ((string?)cell.Attribute("t") == "inlineStr") value = Text(cell);
                    var formula = cell.Element(s + "f")?.Value;
                    var content = formula is null ? value ?? "[valeur absente]" : "formule: =" + formula + " ; valeur mise en cache: " + (string.IsNullOrEmpty(value) ? "indisponible" : value);
                    var address = cell.Attribute("r")?.Value;
                    if (!Add(sheet.FullName + "!" + address, address + ": " + content)) break;
                }
                if (!complete) break;
            }
            // Charts, formatting and formula results are not reconstructed or executed.
            complete = false;
        }
        return new(builder.ToString(), complete, segments);
    }
}
