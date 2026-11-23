using Alycia.Contracts.Fixtures;

namespace Alycia.Contracts.Tests;

public sealed class PublicSurfaceTests
{
    [Fact]
    public void Exported_signatures_match_the_signed_reference()
    {
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "public-api.txt"));
        Assert.Equal(expected, PublicSurface.Read(typeof(ChatSettings).Assembly));
    }
}
