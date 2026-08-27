using System.Collections.Generic;

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
