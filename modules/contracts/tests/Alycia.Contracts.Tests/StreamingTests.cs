using System.Text;

namespace Alycia.Modules.Chat.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task ParserBoundsAnUnterminatedLine()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100)));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var line in BoundedLines.ReadAsync(stream, default, 64)) { }
        });
    }
}
