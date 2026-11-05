using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Data;

namespace MoonBooks.Services;

/// <summary>
/// Watches the library folder. New files are imported; deleted files are marked missing.
/// Enabled by default via <see cref="AppSettings.WatchLibrary"/>.
/// </summary>
public sealed class LibraryWatcher : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IBookImportService _import;
    private readonly IDbContextFactory<BookDbContext> _dbFactory;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    private readonly List<string> _pending = [];

    public LibraryWatcher(ISettingsService settings, IBookImportService import, IDbContextFactory<BookDbContext> dbFactory)
    {
        _settings = settings;
        _import = import;
        _dbFactory = dbFactory;
    }

    public void Start()
    {
        Stop();
        if (!_settings.Current.WatchLibrary)
            return;
        var folder = _settings.Current.LibraryPath;
        if (string.IsNullOrWhiteSpace(folder))
            return;
        Directory.CreateDirectory(folder);
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => Queue(e.FullPath);
        _watcher.Renamed += (_, e) => Queue(e.FullPath);
        _watcher.Deleted += (_, e) => _ = HandleDeletedAsync(e.FullPath, CancellationToken.None);
    }

    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public async Task HandleDeletedAsync(string fullPath, CancellationToken ct)
    {
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(name))
            return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FirstOrDefaultAsync(item => item.FileName == name, ct);
        if (book is null || book.IsMissing)
            return;
        if (File.Exists(Path.Combine(_settings.Current.LibraryPath, name)))
            return;
        book.IsMissing = true;
        await db.SaveChangesAsync(ct);
    }

    private void Queue(string path)
    {
        if (!path.EndsWith(".fb2", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            return;
        lock (_gate)
        {
            _pending.Add(path);
            _debounce?.Cancel();
            _debounce = new CancellationTokenSource();
            var token = _debounce.Token;
            _ = DebounceAsync(token);
        }
    }

    private async Task DebounceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        string[] batch;
        lock (_gate)
        {
            batch = _pending.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _pending.Clear();
        }

        if (batch.Length == 0)
            return;
        await _import.ImportAsync(batch, progress: null, CancellationToken.None);
    }

    public void Dispose()
    {
        Stop();
        _debounce?.Dispose();
    }
}
