using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;
using MoonBooks.Data;

namespace MoonBooks.Services;

public sealed class LibraryService : ILibraryService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly ISettingsService _settings;
    private readonly Subject<LibraryChanged> _changes = new();

    public LibraryService(IDbContextFactory<BookDbContext> factory, ISettingsService settings)
    {
        _factory = factory;
        _settings = settings;
    }

    public IObservable<LibraryChanged> Changes => _changes.AsObservable();

    public void Notify(LibraryChanged change) => _changes.OnNext(change);

    public async Task<IReadOnlyList<Book>> QueryAsync(LibraryQuery query, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        IQueryable<Book> books = db.Books
            .AsNoTracking()
            .Include(book => book.Authors).ThenInclude(link => link.Author)
            .Include(book => book.Categories).ThenInclude(link => link.Category)
            .Include(book => book.ReadingState);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            books = books.Where(book =>
                EF.Functions.Like(book.Title, term) ||
                book.Authors.Any(link => EF.Functions.Like(link.Author.Name, term)) ||
                (book.SeriesName != null && EF.Functions.Like(book.SeriesName, term)));
        }

        if (query.CategoryId is long categoryId)
            books = books.Where(book => book.Categories.Any(link => link.CategoryId == categoryId));
        if (query.Status is BookStatus status)
            books = books.Where(book => book.ReadingState != null && book.ReadingState.Status == status);
        if (query.FavoritesOnly)
            books = books.Where(book => book.Categories.Any(link => link.Category.IsSystem && link.Category.Name == Category.FavoritesName));

        return await books.OrderBy(book => book.SortTitle).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Book>> GetContinueReadingAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books
            .AsNoTracking()
            .Include(book => book.Authors).ThenInclude(link => link.Author)
            .Include(book => book.ReadingState)
            .Where(book => book.ReadingState != null && book.ReadingState.Status == BookStatus.Reading)
            .OrderByDescending(book => book.ReadingState!.UpdatedAtUtc)
            .Take(8)
            .ToListAsync(ct);
    }

    public async Task<Book?> GetBookAsync(long id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books
            .AsNoTracking()
            .Include(book => book.Authors).ThenInclude(link => link.Author)
            .Include(book => book.Categories).ThenInclude(link => link.Category)
            .Include(book => book.ReadingState)
            .FirstOrDefaultAsync(book => book.Id == id, ct);
    }

    public async Task DeleteBookAsync(long id, bool deleteFile, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var book = await db.Books.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (book is null)
            return;
        if (deleteFile)
        {
            var path = Path.Combine(_settings.Current.LibraryPath, book.FileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        db.Books.Remove(book);
        await db.SaveChangesAsync(ct);
        Notify(new LibraryChanged(LibraryChangeKind.Delete, id));
    }

    public async Task SavePositionAsync(long bookId, int chapterIndex, int charOffset, double percent, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var state = await db.ReadingStates.FirstOrDefaultAsync(item => item.BookId == bookId, ct);
        if (state is null)
        {
            state = new ReadingState { BookId = bookId };
            db.ReadingStates.Add(state);
        }

        state.ChapterIndex = Math.Max(0, chapterIndex);
        state.CharOffset = Math.Max(0, charOffset);
        state.Percent = Math.Clamp(percent, 0, 100);
        state.Status = state.Percent >= 99 ? BookStatus.Finished : BookStatus.Reading;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var book = await db.Books.FirstOrDefaultAsync(item => item.Id == bookId, ct);
        if (book is not null)
            book.LastOpenedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
