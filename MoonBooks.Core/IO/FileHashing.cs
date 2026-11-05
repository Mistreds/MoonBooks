using System.Security.Cryptography;

namespace MoonBooks.Core.IO;

public static class FileHashing
{
    public static async Task<string> Sha256Async(Stream stream, CancellationToken ct)
    {
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await Sha256Async(stream, ct);
    }
}
