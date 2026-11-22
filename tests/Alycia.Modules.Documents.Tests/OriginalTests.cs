using System.Security.Cryptography;
using System.Text;

namespace Alycia.Modules.Documents.Tests;

public sealed class OriginalTests
{
    [Fact]
    public async Task Returned_bytes_are_the_verified_snapshot_when_the_path_is_replaced()
    {
        var dir = Directory.CreateTempSubdirectory("alycia-original-");
        try
        {
            var owner = Guid.NewGuid();
            var file = Guid.NewGuid();
            var storage = new FileStorage(dir.FullName);
            Directory.CreateDirectory(storage.DirectoryFor(owner));
            var path = storage.PathFor(owner, file);
            var bytes = Encoding.UTF8.GetBytes("archive");
            await File.WriteAllBytesAsync(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var result = await VerifiedOriginal.OpenAsync(storage, owner, false, file, bytes.Length, hash, default);
            await using var returned = result.Content;
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("changes"));
            using var copy = new MemoryStream();
            await returned.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(() => VerifiedOriginal.OpenAsync(storage, owner, false, file, bytes.Length, hash, default));
        }
        finally { dir.Delete(true); }
    }
}
