using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

/// <summary>
/// Local secret files for platforms without DPAPI. On Unix the file mode is 0600.
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private readonly string _directory;

    public FileSecretStore(AppPaths paths)
        : this(paths.SecretsDir)
    {
    }

    public FileSecretStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task SetAsync(string key, string secret, CancellationToken ct)
    {
        var path = PathFor(key);
        await File.WriteAllTextAsync(path, secret ?? string.Empty, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;
        return await File.ReadAllTextAsync(path, ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        foreach (var ch in key)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
                throw new ArgumentException("Ключ секрета может содержать только буквы, цифры, '-' и '_'.", nameof(key));
        }

        return Path.Combine(_directory, key);
    }
}
