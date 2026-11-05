using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Sync;

namespace MoonBooks.Services;

public sealed class SyncProviderFactory : ISyncProviderFactory
{
    private readonly ISecretStore _secrets;
    private readonly AppPaths _paths;

    public SyncProviderFactory(ISecretStore secrets, AppPaths paths)
    {
        _secrets = secrets;
        _paths = paths;
    }

    public ISyncProvider Create(AppSettings settings)
    {
        return settings.SyncProvider switch
        {
            SyncProviderKind.WebDav => new WebDavSyncProvider(
                settings.WebDavUrl,
                settings.WebDavUser,
                _secrets.GetAsync(SecretKeys.WebDavPassword, CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty),
            SyncProviderKind.GoogleDrive => new GoogleDriveSyncProvider(
                settings.GoogleClientId,
                _secrets.GetAsync(SecretKeys.GoogleClientSecret, CancellationToken.None).GetAwaiter().GetResult(),
                settings.GoogleDriveFolderId,
                Path.Combine(_paths.CacheDir, "google")),
            _ => new DisabledSyncProvider(),
        };
    }

    private sealed class DisabledSyncProvider : ISyncProvider
    {
        public Task TestConnectionAsync(CancellationToken ct) =>
            Task.FromException(new System.InvalidOperationException("Синхронизация выключена."));

        public Task<System.Collections.Generic.IReadOnlyList<RemoteEntry>> ListAsync(string folder, CancellationToken ct) =>
            Task.FromResult<System.Collections.Generic.IReadOnlyList<RemoteEntry>>([]);

        public Task<RemoteEntry?> GetAsync(string path, CancellationToken ct) => Task.FromResult<RemoteEntry?>(null);

        public Task DownloadAsync(string path, Stream target, CancellationToken ct) => Task.CompletedTask;

        public Task UploadAsync(string path, Stream source, CancellationToken ct) => Task.CompletedTask;

        public Task EnsureFolderAsync(string folder, CancellationToken ct) => Task.CompletedTask;
    }
}
