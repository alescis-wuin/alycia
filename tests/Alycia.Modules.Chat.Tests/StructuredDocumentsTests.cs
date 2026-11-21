using System.IO.Compression;
using System.Text;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class StructuredDocumentsTests
{
    [Fact]
    public void FrozenPdfRetainsBothPageCoordinates()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "evaluation/context-v2/manifest.json"))) root = root.Parent;
        Assert.NotNull(root);
        var pdf = StructuredDocuments.Extract("pages.pdf", File.ReadAllBytes(Path.Combine(root.FullName, "evaluation/context-v2/corpus/docs/pages.pdf")));
        Assert.Equal(new[] { "page:1", "page:2" }, pdf.Segments.Select(s => s.Location));
        Assert.Contains("34", pdf.Segments[1].Text);
    }
    [Fact]
    public void OfficePreservesRowsCellsFormulasAndSlidePartsWithoutClaimingFullLayout()
    {
        var word = Zip(("word/document.xml", """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Titre</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Alpha</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>17</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>
            """));
        var docx = StructuredDocuments.Extract("x.docx", word);
        Assert.False(docx.Complete); Assert.Contains("block:2:row:1", docx.Text); Assert.Contains("Alpha | 17", docx.Text);
        var workbook = Zip(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row><c r="B2"><f>A2*3</f><v/></c></row></sheetData></worksheet>
            """));
        var xlsx = StructuredDocuments.Extract("x.xlsx", workbook);
        Assert.Contains("sheet1.xml!B2", xlsx.Text); Assert.Contains("=A2*3", xlsx.Text); Assert.Contains("indisponible", xlsx.Text);
        var slides = Zip(("ppt/slides/slide2.xml", "<slide><p><t>Deux</t></p></slide>"), ("ppt/slides/slide1.xml", "<slide><p><t>Un</t></p></slide>"));
        var pptx = StructuredDocuments.Extract("x.pptx", slides);
        Assert.Equal("ppt/slides/slide1.xml", pptx.Segments[0].Location);
    }
    [Fact]
    public void XmlExternalEntitiesAreRejectedAndOutputRemainsBounded()
    {
        var malicious = Zip(("word/document.xml", "<!DOCTYPE doc [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><doc>&x;</doc>"));
        Assert.ThrowsAny<Exception>(() => StructuredDocuments.Extract("x.docx", malicious));
        var text = new string('é', FileImport.MaxTextBytes);
        var slide = Zip(("ppt/slides/slide1.xml", "<slide><p><t>" + text + "</t></p></slide>"));
        var extracted = StructuredDocuments.Extract("x.pptx", slide);
        Assert.False(extracted.Complete); Assert.InRange(Encoding.UTF8.GetByteCount(extracted.Text), 1, FileImport.MaxTextBytes);
    }
    private static byte[] Zip(params (string Name, string Text)[] parts)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var part in parts)
            {
                using var stream = zip.CreateEntry(part.Name).Open();
                stream.Write(Encoding.UTF8.GetBytes(part.Text));
            }
        return output.ToArray();
    }
}
