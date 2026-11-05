using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace MoonBooks.Services;

public sealed class CoverCache : ICoverCache
{
    public const int MaxEdge = 300;
    private readonly string _directory;

    public CoverCache(AppPaths paths)
        : this(paths.CoversDir)
    {
    }

    public CoverCache(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public string? GetPath(string? cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
            return null;
        var path = Path.Combine(_directory, cacheKey + ".jpg");
        return File.Exists(path) ? path : null;
    }

    public Task<string?> SaveAsync(string hash, byte[]? bytes, CancellationToken ct)
    {
        if (bytes is null || bytes.Length == 0 || string.IsNullOrWhiteSpace(hash))
            return Task.FromResult<string?>(null);

        try
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
                return Task.FromResult<string?>(null);

            var scale = Math.Min(1d, MaxEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            if (resized is null)
                return Task.FromResult<string?>(null);
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
            var path = Path.Combine(_directory, hash + ".jpg");
            using var stream = File.Create(path);
            data.SaveTo(stream);
            return Task.FromResult<string?>(hash);
        }
        catch (Exception)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
