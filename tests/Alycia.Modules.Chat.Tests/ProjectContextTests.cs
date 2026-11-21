using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectContextTests
{
    [Fact]
    public void LargeDocumentationDoesNotHideSourceCodeAndBudgetIsRespected()
    {
        var files = new[] { File("README.md", new string('d', 10000)), File("src/Chat.cs", "public class Chat {}"),
            File("tests/ChatTests.cs", "class ChatTests {}") };
        var result = ProjectContext.Select(files, "Explique le projet", 2048);
        Assert.Contains(result.Sources, s => s.Path == "src/Chat.cs");
        Assert.Contains(result.Sources, s => s.Path == "tests/ChatTests.cs");
        Assert.InRange(Encoding.UTF8.GetByteCount(result.Text), 1, 2048);
        Assert.Equal(3, result.Available);
    }

    [Fact]
    public void FindsRelevantCodeAfterLongHeader()
    {
        var result = ProjectContext.Select([File("src/A.cs", new string('x', 8000) + "\npublic void ProcessMessage() {}")], "ProcessMessage", 1024);
        Assert.Contains("ProcessMessage", result.Text);
        Assert.True(result.Sources.Single().Partial);
    }

    [Fact]
    public void ProjectSourcesRemainEligibleAndExclusionsAlwaysWin()
    {
        var file = File("src/A.cs", "code");
        Assert.False(ProjectContext.Eligible(file, 50, 55));
        file.ContextMode = "project";
        Assert.True(ProjectContext.Eligible(file, 50, 55));
        file.ContextMode = "excluded";
        Assert.False(ProjectContext.Eligible(file, 50, 55));
        Assert.Empty(ProjectContext.Select([file], "code", 1024).Sources);
    }

    private static Attachment File(string path, string text) => new() { Id = Guid.NewGuid(), Name = path, Text = text, AnchorSequence = 1 };
}
