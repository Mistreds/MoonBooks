namespace MoonBooks.Core.Reading;

/// <summary>
/// A stable position inside a book. Page numbers are never stored: a page depends on
/// font, viewport and columns, while a character offset does not.
/// </summary>
public readonly record struct BookPosition(int ChapterIndex, int CharOffset)
{
    public static BookPosition Start => new(0, 0);
}
