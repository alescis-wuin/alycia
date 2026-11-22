namespace Alycia.Modules.Chat.Tests;

public sealed class CodeStructureTests
{
    [Fact]
    public void DistinguishesOverloadsAndPreservesExactSourceRanges()
    {
        const string text = "using System;\nclass Example {\n public int Add(int a) => a + 1;\n public string Add(string a) => a + \"!\";\n public int Run() { return Add(2); }\n}\n";
        var parts = CodeStructure.Split(Guid.NewGuid(), "src/Example.cs", "sha", text);
        Assert.Contains(parts, c => c.Symbol == "Example.Add(int a)");
        Assert.Contains(parts, c => c.Symbol == "Example.Add(string a)");
        Assert.Contains(parts, c => c.Symbol == "Example.Run()" && c.References.Contains("Add"));
        Assert.All(parts, c => Assert.Equal(text[c.Start..c.End], c.Text));
        Assert.Equal(3, parts.Single(c => c.Symbol == "Example.Add(int a)").StartLine);
    }
    [Fact]
    public void LargeMethodsHaveExplicitPartialUnitsAndIncludeTheTail()
    {
        var text = "class C { void Long() {\n" + string.Concat(Enumerable.Repeat(" Console.WriteLine(42);\n", 10000)) + " // TAIL_SENTINEL\n } }";
        var parts = CodeStructure.Split(Guid.NewGuid(), "C.cs", "sha", text);
        Assert.Contains(parts, c => c.Text.Contains("TAIL_SENTINEL"));
        Assert.Contains(parts, c => !c.CompleteUnit && c.Symbol == "C.Long()");
        Assert.All(parts, c => Assert.Equal(text[c.Start..c.End], c.Text));
    }
    [Fact]
    public void OtherLanguagesKeepLineRangesAndExplicitTextKind()
    {
        const string text = "# Intro\nRésumé\n## Décision\nGarder PostgreSQL.\n";
        var parts = CodeStructure.Split(Guid.NewGuid(), "notes.md", "sha", text);
        Assert.Contains(parts, c => c.Symbol == "## Décision" && c.StartLine == 3);
        Assert.All(parts, c => Assert.Equal("text", c.Kind));
    }
}
