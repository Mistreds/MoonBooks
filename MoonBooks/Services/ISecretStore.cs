using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public interface ISecretStore
{
    Task SetAsync(string key, string secret, CancellationToken ct);
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}

public static class SecretKeys
{
    public const string WebDavPassword = "webdav-password";
    public const string GoogleClientSecret = "google-client-secret";
}
