using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;
using MoonBooks.Core.IO;
using MoonBooks.Core.Parsing;
using MoonBooks.Data;

namespace MoonBooks.Services;

public sealed class BookImportService : IBookImportService
{
    public const string RenamedCopyWarning =
        "Копия с другим именем не будет синхронизироваться с Moon+, пока имена файлов не совпадут.";

    private readonly IReadOnlyList<IBookParser> _parsers;
    private readonly ISettingsService _settings;
    private readonly IDbContextFactory<BookDbContext> _dbFactory;
    private readonly ICoverCache _covers;
    private readonly ICategoryService _categories;
    private readonly SemaphoreSlim _persist = new(1, 1);

    public BookImportService(
        IEnumerable<IBookParser> parsers,
        ISettingsService settings,
        IDbContextFactory<BookDbContext> dbFactory,
        ICoverCache covers,
        ICategoryService categories)
    {
        _parsers = parsers.ToList();
        _settings = settings;
        _dbFactory = dbFactory;
        _covers = covers;
        _categories = categories;
    }

    public Task<ImportBatchResult> ImportFolderAsync(string folder, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(folder))
            return Task.FromResult(new ImportBatchResult([]));
        var paths = Directory.EnumerateFiles(folder)
            .Where(path => _parsers.Any(parser => parser.CanParse(path)))
            .ToList();
        return ImportAsync(paths, progress, ct);
    }

    public async Task<ImportBatchResult> ImportAsync(IReadOnlyList<string> paths, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var results = new ImportItemResult[paths.Count];
        var completed = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, paths.Count), new ParallelOptions
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = ct,
        }, async (index, token) =>
        {
            var path = paths[index];
            results[index] = await ImportCoreAsync(path, forcedFileName: null, replaceBookId: null, token);
            var done = Interlocked.Increment(ref completed);
            progress?.Report(new ImportProgress(done, paths.Count, Path.GetFileName(path)));
        });
        return new ImportBatchResult(results);
    }

    public async Task<ImportItemResult> ResolveConflictAsync(ImportConflict conflict, ConflictResolution resolution, CancellationToken ct)
    {
        switch (resolution)
        {
            case ConflictResolution.Skip:
                return new ImportItemResult(conflict.SourcePath, ImportItemStatus.Skipped, null, "Файл пропущен.", null);
            case ConflictResolution.Replace:
                return await ImportCoreAsync(conflict.SourcePath, forcedFileName: conflict.FileName, replaceBookId: conflict.ExistingBookId, ct);
            case ConflictResolution.KeepBoth:
                var name = await NextCopyNameAsync(conflict.FileName, ct);
                var result = await ImportCoreAsync(conflict.SourcePath, forcedFileName: name, replaceBookId: null, ct);
                var message = result.Status == ImportItemStatus.Imported
                    ? RenamedCopyWarning
                    : result.Message;
                return result with { Message = message };
            default:
                return new ImportItemResult(conflict.SourcePath, ImportItemStatus.Failed, null, "Неизвестное решение.", conflict);
        }
    }

    private async Task<ImportItemResult> ImportCoreAsync(string path, string? forcedFileName, long? replaceBookId, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
                return new ImportItemResult(path, ImportItemStatus.Failed, null, "Файл не найден.", null);

            var parser = _parsers.FirstOrDefault(item => item.CanParse(path));
            if (parser is null)
                return new ImportItemResult(path, ImportItemStatus.Skipped, null, "Формат не поддерживается.", null);

            var bytes = await File.ReadAllBytesAsync(path, ct);
            string hash;
            await using (var hashStream = new MemoryStream(bytes, writable: false))
                hash = await FileHashing.Sha256Async(hashStream, ct);

            await using var metaStream = new MemoryStream(bytes, writable: false);
            var metadata = await parser.ReadMetadataAsync(metaStream, ct);
            await using var contentStream = new MemoryStream(bytes, writable: false);
            var content = await parser.ReadContentAsync(contentStream, ct);

            var library = _settings.Current.LibraryPath;
            Directory.CreateDirectory(library);
            var fileName = forcedFileName ?? Path.GetFileName(path);

            await _persist.WaitAsync(ct);
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var sameHash = await db.Books.AsNoTracking().FirstOrDefaultAsync(book => book.FileHash == hash, ct);
                if (sameHash is not null && replaceBookId is null)
                    return new ImportItemResult(path, ImportItemStatus.SkippedDuplicate, sameHash.Id, "Такая книга уже есть.", null);

                var sameName = await db.Books.FirstOrDefaultAsync(book => book.FileName == fileName, ct);
                if (sameName is not null && sameName.Id != replaceBookId && !string.Equals(sameName.FileHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    return new ImportItemResult(
                        path,
                        ImportItemStatus.Conflict,
                        sameName.Id,
                        "Файл с таким именем уже есть, но это другая книга.",
                        new ImportConflict(path, fileName, sameName.Id));
                }

                var destination = Path.Combine(library, fileName);
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                {
                    var partial = destination + ".partial";
                    await File.WriteAllBytesAsync(partial, bytes, ct);
                    File.Move(partial, destination, overwrite: true);
                }

                var coverKey = await _covers.SaveAsync(hash, metadata.CoverBytes, ct);
                Book book;
                if (replaceBookId is not null)
                {
                    book = await db.Books
                        .Include(item => item.Authors)
                        .Include(item => item.Categories)
                        .SingleAsync(item => item.Id == replaceBookId.Value, ct);
                    db.BookAuthors.RemoveRange(book.Authors);
                    db.BookCategories.RemoveRange(book.Categories.Where(link => link.Source == CategorySource.Auto));
                    book.Authors.Clear();
                    book.Categories.RemoveAll(link => link.Source == CategorySource.Auto);
                }
                else if (sameName is not null)
                {
                    book = sameName;
                }
                else
                {
                    book = new Book { AddedAtUtc = DateTime.UtcNow };
                    db.Books.Add(book);
                }

                book.Title = string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(fileName) : metadata.Title.Trim();
                book.SortTitle = Book.BuildSortTitle(book.Title);
                book.Annotation = metadata.Annotation;
                book.Language = metadata.Language;
                book.SeriesName = metadata.Series;
                book.SeriesIndex = metadata.SeriesIndex;
                book.Format = metadata.Format;
                book.FileName = fileName;
                book.FileSize = bytes.LongLength;
                book.FileHash = hash;
                book.CoverCacheKey = coverKey;
                book.ChapterCount = content.Chapters.Count;
                book.TotalChars = content.TotalChars;
                book.IsMissing = false;
                if (book.AddedAtUtc == default)
                    book.AddedAtUtc = DateTime.UtcNow;

                for (var i = 0; i < metadata.Authors.Count; i++)
                {
                    var name = metadata.Authors[i].Trim();
                    if (name.Length == 0)
                        continue;
                    var author = await db.Authors.FirstOrDefaultAsync(item => item.Name == name, ct);
                    if (author is null)
                    {
                        author = new Author { Name = name };
                        db.Authors.Add(author);
                    }

                    book.Authors.Add(new BookAuthor { Author = author, Order = i });
                }

                await _categories.EnsureAutoGenresAsync(db, book, metadata, ct);
                await db.SaveChangesAsync(ct);
                return new ImportItemResult(path, ImportItemStatus.Imported, book.Id, null, null);
            }
            finally
            {
                _persist.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ImportItemResult(path, ImportItemStatus.Failed, null, ex.Message, null);
        }
    }

    private async Task<string> NextCopyNameAsync(string fileName, CancellationToken ct)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var names = await db.Books.Select(book => book.FileName).ToListAsync(ct);
        var taken = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!taken.Contains(candidate))
                return candidate;
        }

        return $"{stem} ({Guid.NewGuid():N}){ext}";
    }
}
