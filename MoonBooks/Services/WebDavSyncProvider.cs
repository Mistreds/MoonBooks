using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Sync;
using WebDav;

namespace MoonBooks.Services;

public sealed class WebDavSyncProvider : ISyncProvider
{
    private readonly WebDavClient _client;
    private readonly Uri _baseUri;

    public WebDavSyncProvider(string url, string login, string password)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("WebDAV не настроен: укажите адрес сервера.");
        if (!url.EndsWith('/'))
            url += "/";
        _baseUri = new Uri(url, UriKind.Absolute);
        _client = new WebDavClient(new WebDavClientParams
        {
            BaseAddress = _baseUri,
            Credentials = new NetworkCredential(login, password),
        });
    }

    public async Task TestConnectionAsync(CancellationToken ct)
    {
        var response = await _client.Propfind(_baseUri);
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"WebDAV: {response.StatusCode} {response.Description}");
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string folder, CancellationToken ct)
    {
        var response = await _client.Propfind(ToUri(folder));
        if (response.StatusCode == 404)
            return [];
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"WebDAV PROPFIND: {response.StatusCode} {response.Description}");

        return response.Resources
            .Where(resource => !resource.IsCollection)
            .Select(resource => new RemoteEntry(
                Normalize(resource.Uri),
                resource.ContentLength ?? 0,
                resource.LastModifiedDate,
                resource.ETag))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .ToList();
    }

    public async Task<RemoteEntry?> GetAsync(string path, CancellationToken ct)
    {
        var response = await _client.Propfind(ToUri(path));
        if (response.StatusCode == 404)
            return null;
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"WebDAV: {response.StatusCode}");
        var resource = response.Resources.FirstOrDefault(item => !item.IsCollection) ?? response.Resources.FirstOrDefault();
        if (resource is null)
            return null;
        return new RemoteEntry(Normalize(resource.Uri), resource.ContentLength ?? 0, resource.LastModifiedDate, resource.ETag);
    }

    public async Task DownloadAsync(string path, Stream target, CancellationToken ct)
    {
        using var response = await _client.GetRawFile(ToUri(path));
        if (!response.IsSuccessful || response.Stream is null)
            throw new InvalidOperationException("Не удалось скачать файл WebDAV.");
        await response.Stream.CopyToAsync(target, ct);
    }

    public async Task UploadAsync(string path, Stream source, CancellationToken ct)
    {
        if (source.CanSeek)
            source.Position = 0;
        var response = await _client.PutFile(ToUri(path), source);
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"WebDAV PUT: {response.StatusCode} {response.Description}");
    }

    public async Task EnsureFolderAsync(string folder, CancellationToken ct)
    {
        var current = "";
        foreach (var segment in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : current + "/" + segment;
            var response = await _client.Mkcol(ToUri(current));
            if (response.IsSuccessful || response.StatusCode is 405 or 409 or 301)
                continue;
        }
    }

    private Uri ToUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            return absolute;
        return new Uri(_baseUri, path.TrimStart('/'));
    }

    private string Normalize(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return string.Empty;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
            return Uri.UnescapeDataString(_baseUri.MakeRelativeUri(absolute).ToString());
        return uri.TrimStart('/');
    }
}
