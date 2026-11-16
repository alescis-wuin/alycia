using System.Text;

namespace Alycia.Modules.Chat.Application;

public static class BoundedLines
{
    public static async IAsyncEnumerable<string> ReadAsync(Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, int maxLength = 131072)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var buffer = new char[4096];
        var line = new StringBuilder();
        int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            for (var i = 0; i < length; i++)
            {
                if (buffer[i] == '\n') { yield return line.ToString(); line.Clear(); }
                else
                {
                    if (line.Length >= maxLength) throw new InvalidDataException("Ligne du flux trop volumineuse.");
                    line.Append(buffer[i]);
                }
            }
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
