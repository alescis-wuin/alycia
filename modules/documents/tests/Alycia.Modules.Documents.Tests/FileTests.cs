using System.IO.Compression;
using System.Text;

namespace Alycia.Modules.Chat.Tests;

public sealed class FileTests
{
    [Theory]
    [InlineData("../secret")]
    [InlineData("a/../../secret")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\private")]
    [InlineData("C:relative.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("file:///etc/passwd")]
    [InlineData("safe/./../secret")]
    [InlineData("a\0b")]
    public void RejectsUnsafePaths(string path) => Assert.Throws<ArgumentException>(() => FileImport.NormalizeName(path));

    [Theory]
    [InlineData("./projet/src/./data.json", "projet/src/data.json")]
    [InlineData(".\\projet\\src\\data.json", "projet/src/data.json")]
    [InlineData("projet//src/data.json", "projet/src/data.json")]
    [InlineData("projet/rapport:2026-11-16.json", "projet/rapport:2026-11-16.json")]
    [InlineData("projet/été 🦊.json", "projet/été 🦊.json")]
    public void CanonicalizesBenignRelativePathsAndPreservesUnicode(string source, string expected) =>
        Assert.Equal(expected, FileImport.NormalizeName(source));

    [Fact]
    public void ReadsUtf8AndDocumentsButNeverImages()
    {
        Assert.Equal("été", FileImport.Extract("notes.custom", Encoding.UTF8.GetBytes("été")).Text);
        Assert.Empty(FileImport.Extract("image.svg", Encoding.UTF8.GetBytes("<svg>secret</svg>")).Text);
        Assert.Empty(FileImport.Extract("binary.exe", [0, 1, 2, 255]).Text);
        var docx = Zip("word/document.xml", "<w:document xmlns:w='urn:word'><w:p><w:t>Bonjour document</w:t></w:p></w:document>");
        Assert.Contains("Bonjour document", FileImport.Extract("note.docx", docx).Text);
        var hostile = Zip("word/document.xml", "<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///etc/passwd'>]><x>&ext;</x>");
        Assert.Empty(FileImport.Extract("bad.docx", hostile).Text);
        var large = FileImport.Extract("large.txt", Encoding.UTF8.GetBytes(new string('é', 200000)));
        Assert.True(Encoding.UTF8.GetByteCount(large.Text) <= FileImport.MaxTextBytes);
        Assert.Contains("partiellement", large.Status);
        using var xlsx = new MemoryStream();
        using (var archive = new ZipArchive(xlsx, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("xl/sharedStrings.xml").Open()))
                writer.Write("<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>Budget projet</t></si></sst>");
            using (var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open()))
                writer.Write("<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c r='A1' t='s'><v>0</v></c><c r='B1'><v>42</v></c></row></sheetData></worksheet>");
        }
        var spreadsheet = FileImport.Extract("budget.xlsx", xlsx.ToArray()).Text;
        Assert.Contains("A1: Budget projet", spreadsheet);
        Assert.Contains("B1: 42", spreadsheet);
    }

    private static byte[] Zip(string name, string text, bool windowsDirectory = false)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            if (windowsDirectory) archive.CreateEntry(".\\folder\\");
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        return output.ToArray();
    }
}
