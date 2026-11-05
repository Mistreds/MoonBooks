using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Parsing;
using MoonBooks.Core.Sync;
using MoonBooks.Data;

namespace MoonBooks.Services;

public interface ISyncProviderFactory
{
    ISyncProvider Create(AppSettings settings);
}

public sealed class SyncEngine
{
    private readonly ISettingsService _settings;
    private readonly IDbContextFactory<BookDbContext> _dbFactory;
    private readonly ISyncProviderFactory _providers;
    private readonly IMoonPlusPositionMapper _mapper;
    private readonly IEnumerable<IBookParser> _parsers;
    private readonly IBookImportService _import;

    public SyncEngine(
        ISettingsService settings,
        IDbContextFactory<BookDbContext> dbFactory,
        ISyncProviderFactory providers,
        IMoonPlusPositionMapper mapper,
        IEnumerable<IBookParser> parsers,
        IBookImportService import)
    {
        _settings = settings;
        _dbFactory = dbFactory;
        _providers = providers;
        _mapper = mapper;
        _parsers = parsers;
        _import = import;
    }

    public async Task<SyncRunResult> RunAsync(CancellationToken ct)
    {
        var settings = _settings.Current;
        if (settings.SyncProvider == SyncProviderKind.None)
            return new SyncRunResult { Disabled = true };

        var provider = _providers.Create(settings);
        try
        {
            await provider.TestConnectionAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await EnqueueDirtyPositionsAsync(ct, ex.Message);
            return new SyncRunResult { Offline = true, Error = ex.Message };
        }

        await FlushQueueAsync(provider, settings, ct);
        var prompts = await SyncPositionsAsync(provider, settings, ct);
        if (settings.SyncMode == SyncMode.StatusAndBooks)
            await SyncBooksAsync(provider, settings, ct);
        await SyncLibraryMetaAsync(provider, settings, ct);
        return new SyncRunResult { Prompts = prompts };
    }

    public async Task ApplyRemoteAsync(PositionPrompt prompt, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.Include(item => item.ReadingState).SingleAsync(item => item.Id == prompt.BookId, ct);
        var content = await TryReadContentAsync(book, ct);
        var position = _mapper.ToBookPosition(prompt.Remote, book, content);
        Apply(book, position, prompt.Remote.Percent, prompt.Remote.TimestampUnixMs, synced: true);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<PositionPrompt>> SyncPositionsAsync(ISyncProvider provider, AppSettings settings, CancellationToken ct)
    {
        var folder = CacheFolder(settings);
        await provider.EnsureFolderAsync(folder, ct);
        var entries = await provider.ListAsync(folder, ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var books = await db.Books.Include(book => book.ReadingState).ToListAsync(ct);
        var prompts = new List<PositionPrompt>();
        var remoteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry.Path);
            if (!name.EndsWith(".po", StringComparison.OrdinalIgnoreCase))
                continue;
            var fileName = name[..^3];
            remoteNames.Add(fileName);
            var book = books.FirstOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (book is null)
                continue;

            using var buffer = new MemoryStream();
            await provider.DownloadAsync(entry.Path, buffer, ct);
            var body = Encoding.UTF8.GetString(buffer.ToArray()).Trim();
            if (body.Length == 0)
                continue;
            MoonPlusPosition remote;
            try
            {
                remote = MoonPlusPositionCodec.Parse(body);
            }
            catch (FormatException)
            {
                continue;
            }

            var localStamp = book.ReadingState?.UpdatedAtUtc ?? 0;
            var localPercent = book.ReadingState?.Percent ?? 0;
            if (remote.TimestampUnixMs > localStamp)
            {
                if (!settings.AutoAcceptRemotePosition && Math.Abs(remote.Percent - localPercent) > 5)
                {
                    prompts.Add(new PositionPrompt(book.Id, book.Title, remote.Percent, localPercent, remote));
                    continue;
                }

                var content = await TryReadContentAsync(book, ct);
                var position = _mapper.ToBookPosition(remote, book, content);
                Apply(book, position, remote.Percent, remote.TimestampUnixMs, synced: true);
            }
            else if (book.ReadingState is not null && localStamp > remote.TimestampUnixMs && localStamp > (book.ReadingState.SyncedAtUtc ?? 0))
            {
                await UploadPositionAsync(provider, settings, book, ct);
            }
        }

        foreach (var book in books)
        {
            if (book.ReadingState is null || remoteNames.Contains(book.FileName))
                continue;
            if (book.ReadingState.UpdatedAtUtc > (book.ReadingState.SyncedAtUtc ?? 0))
                await UploadPositionAsync(provider, settings, book, ct);
        }

        await db.SaveChangesAsync(ct);
        return prompts;
    }

    private async Task SyncBooksAsync(ISyncProvider provider, AppSettings settings, CancellationToken ct)
    {
        var folder = settings.MoonPlusBooksFolder.Trim('/');
        await provider.EnsureFolderAsync(folder, ct);
        var remote = await provider.ListAsync(folder, ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var local = await db.Books.AsNoTracking().ToListAsync(ct);
        var localByName = local.ToDictionary(book => book.FileName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in remote)
        {
            var name = Path.GetFileName(entry.Path);
            if (!name.EndsWith(".fb2", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                continue;
            if (localByName.TryGetValue(name, out var existing) && existing.FileSize == entry.Size)
                continue;

            var destination = Path.Combine(settings.LibraryPath, name);
            Directory.CreateDirectory(settings.LibraryPath);
            await using (var file = File.Create(destination))
                await provider.DownloadAsync(entry.Path, file, ct);
            await _import.ImportAsync([destination], null, ct);
        }

        var remoteNames = new HashSet<string>(remote.Select(entry => Path.GetFileName(entry.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var book in local.Where(book => !book.IsMissing))
        {
            if (remoteNames.Contains(book.FileName))
                continue;
            var path = Path.Combine(settings.LibraryPath, book.FileName);
            if (!File.Exists(path))
                continue;
            await using var source = File.OpenRead(path);
            await provider.UploadAsync(folder + "/" + book.FileName, source, ct);
        }
    }

    private async Task SyncLibraryMetaAsync(ISyncProvider provider, AppSettings settings, CancellationToken ct)
    {
        var folder = settings.MoonPlusBooksFolder.Trim('/') + "/.MoonBooks";
        await provider.EnsureFolderAsync(folder, ct);
        var path = folder + "/moonbooks.json";
        MoonBooksSyncDocument? remote = null;
        var existing = await provider.GetAsync(path, ct);
        if (existing is not null)
        {
            using var buffer = new MemoryStream();
            await provider.DownloadAsync(path, buffer, ct);
            try
            {
                remote = JsonSerializer.Deserialize<MoonBooksSyncDocument>(buffer.ToArray(), SettingsService.JsonOptions);
            }
            catch (JsonException)
            {
                remote = null;
            }
        }

        if (remote is not null)
            await MergeMetaAsync(remote, ct);

        var local = await BuildMetaAsync(ct);
        var payload = JsonSerializer.SerializeToUtf8Bytes(local, SettingsService.JsonOptions);
        await using var upload = new MemoryStream(payload);
        await provider.UploadAsync(path, upload, ct);
    }

    private async Task MergeMetaAsync(MoonBooksSyncDocument remote, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        foreach (var item in remote.Categories.Where(category => !category.IsSystem))
        {
            if (!Enum.TryParse<CategoryKind>(item.Kind, out var kind))
                continue;
            var category = await db.Categories.FirstOrDefaultAsync(existing => existing.Kind == kind && existing.Name == item.Name, ct);
            if (category is null)
            {
                db.Categories.Add(new Category
                {
                    Name = item.Name,
                    Kind = kind,
                    SourceCode = item.SourceCode,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var assignment in remote.Assignments)
        {
            var book = await db.Books.FirstOrDefaultAsync(item => item.FileName == assignment.FileName, ct);
            var category = await db.Categories.FirstOrDefaultAsync(item => item.Name == assignment.CategoryName, ct);
            if (book is null || category is null)
                continue;
            var linked = await db.BookCategories.AnyAsync(link => link.BookId == book.Id && link.CategoryId == category.Id, ct);
            if (!linked)
            {
                db.BookCategories.Add(new BookCategory
                {
                    BookId = book.Id,
                    CategoryId = category.Id,
                    Source = Enum.TryParse<CategorySource>(assignment.Source, out var source) ? source : CategorySource.Manual,
                });
            }
        }

        if (remote.Reader is not null && remote.ReaderUpdatedAtUtc > 0)
        {
            await _settings.UpdateAsync(settings =>
            {
                settings.Reader = remote.Reader;
            }, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<MoonBooksSyncDocument> BuildMetaAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var links = await db.BookCategories.AsNoTracking().Include(link => link.Book).Include(link => link.Category).ToListAsync(ct);
        return new MoonBooksSyncDocument
        {
            SchemaVersion = 1,
            UpdatedAtUtc = now,
            Reader = _settings.Current.Reader,
            ReaderUpdatedAtUtc = now,
            Categories = categories.Select(category => new SyncCategoryItem
            {
                Name = category.Name,
                Kind = category.Kind.ToString(),
                SourceCode = category.SourceCode,
                IsSystem = category.IsSystem,
                UpdatedAtUtc = now,
            }).ToList(),
            Assignments = links.Select(link => new SyncAssignmentItem
            {
                FileName = link.Book.FileName,
                CategoryName = link.Category.Name,
                Source = link.Source.ToString(),
                UpdatedAtUtc = now,
            }).ToList(),
        };
    }

    private async Task UploadPositionAsync(ISyncProvider provider, AppSettings settings, Book book, CancellationToken ct)
    {
        var state = book.ReadingState;
        if (state is null)
            return;
        var content = await TryReadContentAsync(book, ct);
        var position = _mapper.ToMoonPlus(new Core.Reading.BookPosition(state.ChapterIndex, state.CharOffset), book, content, state.UpdatedAtUtc);
        var body = MoonPlusPositionCodec.Format(position);
        var bytes = Encoding.UTF8.GetBytes(body);
        await using var stream = new MemoryStream(bytes);
        await provider.UploadAsync(CacheFolder(settings) + "/" + book.FileName + ".po", stream, ct);
        state.SyncedAtUtc = state.UpdatedAtUtc;
    }

    private async Task EnqueueDirtyPositionsAsync(CancellationToken ct, string error)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var dirty = await db.ReadingStates
            .Where(state => state.SyncedAtUtc == null || state.UpdatedAtUtc > state.SyncedAtUtc)
            .ToListAsync(ct);
        var queued = await db.SyncQueue.Where(item => item.Kind == SyncQueueKind.UploadPosition).Select(item => item.BookId).ToListAsync(ct);
        foreach (var state in dirty)
        {
            if (queued.Contains(state.BookId))
                continue;
            db.SyncQueue.Add(new SyncQueueItem
            {
                Kind = SyncQueueKind.UploadPosition,
                BookId = state.BookId,
                Payload = state.BookId.ToString(),
                Attempts = 0,
                NextAttemptUtc = DateTime.UtcNow,
                LastError = error,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task FlushQueueAsync(ISyncProvider provider, AppSettings settings, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var due = await db.SyncQueue.Where(item => item.NextAttemptUtc <= DateTime.UtcNow).OrderBy(item => item.Id).ToListAsync(ct);
        foreach (var item in due)
        {
            try
            {
                if (item.Kind == SyncQueueKind.UploadPosition && item.BookId is long bookId)
                {
                    var book = await db.Books.Include(entity => entity.ReadingState).FirstOrDefaultAsync(entity => entity.Id == bookId, ct);
                    if (book?.ReadingState is not null)
                        await UploadPositionAsync(provider, settings, book, ct);
                }

                db.SyncQueue.Remove(item);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.Attempts++;
                item.LastError = ex.Message;
                var seconds = Math.Min(1800, Math.Pow(2, Math.Min(item.Attempts, 10)));
                item.NextAttemptUtc = DateTime.UtcNow.AddSeconds(seconds);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Core.Content.BookContent?> TryReadContentAsync(Book book, CancellationToken ct)
    {
        var path = Path.Combine(_settings.Current.LibraryPath, book.FileName);
        var parser = _parsers.FirstOrDefault(item => item.CanParse(path));
        if (parser is null || !File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        return await parser.ReadContentAsync(stream, ct);
    }

    private static void Apply(Book book, Core.Reading.BookPosition position, double percent, long stamp, bool synced)
    {
        book.ReadingState ??= new ReadingState { BookId = book.Id };
        book.ReadingState.ChapterIndex = position.ChapterIndex;
        book.ReadingState.CharOffset = position.CharOffset;
        book.ReadingState.Percent = percent;
        book.ReadingState.Status = percent >= 99 ? BookStatus.Finished : BookStatus.Reading;
        book.ReadingState.UpdatedAtUtc = stamp;
        if (synced)
            book.ReadingState.SyncedAtUtc = stamp;
    }

    public static string CacheFolder(AppSettings settings) =>
        settings.MoonPlusBooksFolder.Trim('/') + "/.Moon+/Cache";
}
