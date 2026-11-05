namespace MoonBooks.Core.Domain;

public sealed class Book
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SortTitle { get; set; } = string.Empty;
    public string? Annotation { get; set; }
    public string? Language { get; set; }
    public string? SeriesName { get; set; }
    public double? SeriesIndex { get; set; }
    public BookFormat Format { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string? CoverCacheKey { get; set; }
    public int ChapterCount { get; set; }
    public int TotalChars { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime? LastOpenedAtUtc { get; set; }
    public bool IsMissing { get; set; }

    public List<BookAuthor> Authors { get; set; } = [];
    public List<BookCategory> Categories { get; set; } = [];
    public ReadingState? ReadingState { get; set; }

    public static string BuildSortTitle(string title) => title.Trim().ToLowerInvariant();
}
