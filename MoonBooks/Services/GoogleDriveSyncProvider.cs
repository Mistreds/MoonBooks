using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using MoonBooks.Core.Sync;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace MoonBooks.Services;

/// <summary>
/// Desktop loopback OAuth for Google Drive. Client id and secret are supplied by the user
/// and are empty by default. drive.file cannot see Moon+ files; the restricted drive scope is required.
/// </summary>
public sealed class GoogleDriveSyncProvider : ISyncProvider
{
    public const string NotConfiguredMessage =
        "Google Drive не настроен. Укажите client id и секрет. Scope drive.file не видит файлы Moon+ — нужен ограниченный scope drive.";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _folderId;
    private readonly string _tokenDirectory;
    private DriveService? _service;
    private Dictionary<string, string> _idsByName = new(StringComparer.OrdinalIgnoreCase);

    public GoogleDriveSyncProvider(string? clientId, string? clientSecret, string? folderId, string tokenDirectory)
    {
        _clientId = clientId ?? string.Empty;
        _clientSecret = clientSecret ?? string.Empty;
        _folderId = folderId ?? string.Empty;
        _tokenDirectory = tokenDirectory;
    }

    public async Task TestConnectionAsync(CancellationToken ct)
    {
        EnsureConfigured();
        var service = await ConnectAsync(ct);
        var request = service.Files.Get(_folderId);
        request.Fields = "id, name";
        await request.ExecuteAsync(ct);
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string folder, CancellationToken ct)
    {
        var service = await ConnectAsync(ct);
        var parent = await ResolveFolderAsync(service, folder, ct);
        var request = service.Files.List();
        request.Q = $"'{parent}' in parents and trashed = false";
        request.Fields = "files(id, name, size, modifiedTime, md5Checksum)";
        request.PageSize = 200;
        var response = await request.ExecuteAsync(ct);
        var files = response.Files ?? [];
        foreach (var file in files)
            _idsByName[file.Name] = file.Id;
        return files.Select(file => new RemoteEntry(
            file.Name,
            file.Size ?? 0,
            file.ModifiedTimeDateTimeOffset?.UtcDateTime,
            file.Md5Checksum)).ToList();
    }

    public async Task<RemoteEntry?> GetAsync(string path, CancellationToken ct)
    {
        var name = Path.GetFileName(path);
        var entries = await ListAsync(Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty, ct);
        return entries.FirstOrDefault(entry => string.Equals(entry.Path, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    public async Task DownloadAsync(string path, Stream target, CancellationToken ct)
    {
        var service = await ConnectAsync(ct);
        var id = await RequireIdAsync(service, path, ct);
        var progress = await service.Files.Get(id).DownloadAsync(target, ct);
        if (progress.Status == Google.Apis.Download.DownloadStatus.Failed)
            throw new IOException(progress.Exception?.Message ?? "Не удалось скачать файл Google Drive.");
    }

    public async Task UploadAsync(string path, Stream source, CancellationToken ct)
    {
        var service = await ConnectAsync(ct);
        var name = Path.GetFileName(path);
        var parent = await ResolveFolderAsync(service, Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty, ct);
        if (source.CanSeek)
            source.Position = 0;
        if (_idsByName.TryGetValue(name, out var existing))
        {
            var update = service.Files.Update(new DriveFile(), existing, source, "application/octet-stream");
            var updated = await update.UploadAsync(ct);
            if (updated.Status == UploadStatus.Failed)
                throw new IOException(updated.Exception?.Message ?? "Не удалось обновить файл Google Drive.");
            return;
        }

        var metadata = new DriveFile { Name = name, Parents = [parent] };
        var create = service.Files.Create(metadata, source, "application/octet-stream");
        create.Fields = "id, name";
        var created = await create.UploadAsync(ct);
        if (created.Status == UploadStatus.Failed)
            throw new IOException(created.Exception?.Message ?? "Не удалось отправить файл Google Drive.");
        if (create.ResponseBody?.Id is string id)
            _idsByName[name] = id;
    }

    public async Task EnsureFolderAsync(string folder, CancellationToken ct)
    {
        var service = await ConnectAsync(ct);
        await ResolveFolderAsync(service, folder, create: true, ct);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret) || string.IsNullOrWhiteSpace(_folderId))
            throw new InvalidOperationException(NotConfiguredMessage);
    }

    private async Task<DriveService> ConnectAsync(CancellationToken ct)
    {
        EnsureConfigured();
        if (_service is not null)
            return _service;
        Directory.CreateDirectory(_tokenDirectory);
        var secrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret };
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            [DriveService.Scope.Drive],
            "moonbooks",
            ct,
            new FileDataStore(_tokenDirectory, fullPath: true));
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "MoonBooks",
        });
        return _service;
    }

    private async Task<string> ResolveFolderAsync(DriveService service, string folder, CancellationToken ct) =>
        await ResolveFolderAsync(service, folder, create: false, ct);

    private async Task<string> ResolveFolderAsync(DriveService service, string folder, bool create, CancellationToken ct)
    {
        var current = _folderId;
        foreach (var segment in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is ".Moon+" or ".MoonBooks" or "Cache")
            {
                current = await FindOrCreateChildAsync(service, current, segment, create || true, ct);
                continue;
            }

            current = await FindOrCreateChildAsync(service, current, segment, create, ct);
        }

        return current;
    }

    private static async Task<string> FindOrCreateChildAsync(DriveService service, string parent, string name, bool create, CancellationToken ct)
    {
        var list = service.Files.List();
        var escaped = name.Replace("'", "\\'");
        list.Q = $"'{parent}' in parents and name = '{escaped}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        list.Fields = "files(id, name)";
        var found = (await list.ExecuteAsync(ct)).Files?.FirstOrDefault();
        if (found is not null)
            return found.Id;
        if (!create)
            throw new DirectoryNotFoundException($"Папка Google Drive «{name}» не найдена.");
        var created = await service.Files.Create(new DriveFile
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder",
            Parents = [parent],
        }).ExecuteAsync(ct);
        return created.Id;
    }

    private async Task<string> RequireIdAsync(DriveService service, string path, CancellationToken ct)
    {
        var name = Path.GetFileName(path);
        if (_idsByName.TryGetValue(name, out var id))
            return id;
        await ListAsync(Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty, ct);
        if (_idsByName.TryGetValue(name, out id))
            return id;
        throw new FileNotFoundException("Файл не найден на Google Drive.", name);
    }
}
