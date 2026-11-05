using Alycia.Modules.Workspaces.Domain;

namespace Alycia.Modules.Workspaces.Tests;

public sealed class WorkspaceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Create_RejectsMissingOrBlankName(string? name)
    {
        Assert.Throws<ArgumentException>(() => Workspace.Create(name));
    }

    [Fact]
    public void Create_TrimsNameAndGeneratesNonEmptyIdAndUtcTime()
    {
        var before = DateTimeOffset.UtcNow;
        var workspace = Workspace.Create("  Personal  ");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal("Personal", workspace.Name);
        Assert.NotEqual(Guid.Empty, workspace.Id);
        Assert.Equal(TimeSpan.Zero, workspace.CreatedAtUtc.Offset);
        Assert.InRange(workspace.CreatedAtUtc, before, after);
    }

    [Fact]
    public void Create_AcceptsExactlyOneHundredCharactersAfterTrimming()
    {
        var workspace = Workspace.Create("  " + new string('x', 100) + "  ");
        Assert.Equal(100, workspace.Name.Length);
    }

    [Fact]
    public void Create_RejectsMoreThanOneHundredCharactersAfterTrimming()
    {
        Assert.Throws<ArgumentException>(() =>
            Workspace.Create(" " + new string('x', 101) + " "));
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(() =>
            new Workspace(Guid.Empty, "Personal", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_RejectsNonUtcAndDefaultTime()
    {
        Assert.Throws<ArgumentException>(() =>
            new Workspace(Guid.NewGuid(), "Personal", default));
        Assert.Throws<ArgumentException>(() =>
            new Workspace(
                Guid.NewGuid(),
                "Personal",
                new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.FromHours(1))));
    }
}
