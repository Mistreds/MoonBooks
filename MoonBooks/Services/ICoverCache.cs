using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public interface ICoverCache
{
    Task<string?> SaveAsync(string hash, byte[]? bytes, CancellationToken ct);
    string? GetPath(string? cacheKey);
}
