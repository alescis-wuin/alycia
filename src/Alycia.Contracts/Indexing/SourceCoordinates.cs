using System.Security.Cryptography;
using System.Text;

namespace Alycia.Contracts.Indexing;

public static class SourceCoordinates
{
    // Version the coordinate system when extraction changes; old diagnostics keep their recorded IDs.
    public static string Reference(Guid fileId, string sha, int start, int end) => "S" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"coordinates-v2:{fileId}:{sha}:{start}:{end}")))[..12];
}
