using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

/// <summary>
/// Windows DPAPI store. ProtectedData is not available on Linux; the app registers
/// <see cref="FileSecretStore"/> there instead of pretending this works.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public DpapiSecretStore(AppPaths paths)
        : this(paths.SecretsDir)
    {
    }

    public DpapiSecretStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public Task SetAsync(string key, string secret, CancellationToken ct)
    {
        EnsureWindows();
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret ?? string.Empty),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), protectedBytes);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        EnsureWindows();
        var path = PathFor(key);
        if (!File.Exists(path))
            return Task.FromResult<string?>(null);
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI (ProtectedData) доступен только в Windows.");
    }

    private string PathFor(string key) => Path.Combine(_directory, key + ".dpapi");
}
