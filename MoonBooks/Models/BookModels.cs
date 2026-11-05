using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MoonBooks.Models;

public sealed record BookChapter(string Title, string Eyebrow, IReadOnlyList<string> Paragraphs);

public sealed record BookItem(
    string Id,
    string Title,
    string Author,
    string Format,
    string Shelf,
    string Category,
    string Description,
    string CoverStart,
    string CoverEnd,
    string CoverMark,
    double Progress,
    string ProgressLabel,
    bool IsFavorite,
    IReadOnlyList<BookChapter> Chapters);

public sealed record ShelfItem(string Name, int Count);

public enum SyncState
{
    Ready,
    Syncing,
    Error,
}


public class BookModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public BookFormat Format { get; set; }
    
    public List<Tag> Tags { get; set; } = [];
    public double Progress { get; set; }
}

public enum BookFormat
{
    Epub,
    Fb2,
}

public enum BookStatus
{
    NotReading,
    Reading,
    Finished,
}

public class Tag
{
    
    public string Name { get; set; } = string.Empty;
}

