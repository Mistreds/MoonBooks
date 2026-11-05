using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;
using MoonBooks.Data;

namespace MoonBooks.Tests;

public class BookDbContextTests : IDisposable
{
    private readonly string _path;
    private readonly BookDbContext _db;

    public BookDbContextTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"moonbooks-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;
        _db = new BookDbContext(options);
        _db.Database.Migrate();
    }

    [Fact]
    public async Task Crud_book_with_author_category_and_reading_state()
    {
        var author = new Author { Name = "Элин Рей" };
        var genre = new Category { Name = "Фэнтези", Kind = CategoryKind.Genre, SourceCode = "sf_fantasy" };
        var book = new Book
        {
            Title = "Маяк",
            SortTitle = Book.BuildSortTitle("Маяк"),
            Annotation = "Аннотация",
            Language = "ru",
            SeriesName = "Север",
            SeriesIndex = 1,
            Format = BookFormat.Fb2,
            FileName = "Mayak.fb2",
            FileSize = 1200,
            FileHash = new string('a', 64),
            ChapterCount = 3,
            TotalChars = 4000,
            AddedAtUtc = DateTime.UtcNow,
        };
        book.Authors.Add(new BookAuthor { Author = author, Order = 0 });
        book.Categories.Add(new BookCategory { Category = genre, Source = CategorySource.Auto });
        book.ReadingState = new ReadingState
        {
            Status = BookStatus.Reading,
            ChapterIndex = 1,
            CharOffset = 42,
            Percent = 15,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var loaded = await _db.Books
            .Include(item => item.Authors).ThenInclude(link => link.Author)
            .Include(item => item.Categories).ThenInclude(link => link.Category)
            .Include(item => item.ReadingState)
            .SingleAsync();

        Assert.Equal("Маяк", loaded.Title);
        Assert.Equal("Элин Рей", loaded.Authors[0].Author.Name);
        Assert.Equal(CategorySource.Auto, loaded.Categories[0].Source);
        Assert.Equal(42, loaded.ReadingState!.CharOffset);

        loaded.Title = "Маяк на краю";
        loaded.ReadingState.Percent = 40;
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var updated = await _db.Books.Include(item => item.ReadingState).SingleAsync();
        Assert.Equal("Маяк на краю", updated.Title);
        Assert.Equal(40, updated.ReadingState!.Percent);

        _db.Books.Remove(updated);
        await _db.SaveChangesAsync();
        Assert.Equal(0, await _db.Books.CountAsync());
        Assert.Equal(0, await _db.ReadingStates.CountAsync());
        Assert.Equal(0, await _db.BookAuthors.CountAsync());
    }

    [Fact]
    public async Task FileName_unique_index_is_case_insensitive()
    {
        _db.Books.Add(Sample("Story.fb2", "b"));
        await _db.SaveChangesAsync();

        _db.Books.Add(Sample("story.fb2", "c"));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task FileHash_is_unique()
    {
        var hash = new string('d', 64);
        _db.Books.Add(Sample("One.fb2", hash));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _db.Books.Add(Sample("Two.fb2", hash));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    private static Book Sample(string fileName, string hashTail) => new()
    {
        Title = fileName,
        SortTitle = Book.BuildSortTitle(fileName),
        Format = BookFormat.Epub,
        FileName = fileName,
        FileHash = hashTail.Length == 64 ? hashTail : hashTail.PadRight(64, '0'),
        AddedAtUtc = DateTime.UtcNow,
    };

    public void Dispose()
    {
        _db.Dispose();
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
