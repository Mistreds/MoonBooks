namespace MoonBooks.Core.Content;

public sealed record TocEntry(string Title, int ChapterIndex, int CharOffset);

public sealed record BookResource(string Id, string MediaType, byte[] Data);

public sealed class ChapterContent
{
    public required string Title { get; init; }
    public required IReadOnlyList<ContentBlock> Blocks { get; init; }
    public string PlainText { get; init; } = string.Empty;
    public int PlainTextLength { get; init; }

    public static ChapterContent Create(string title, IReadOnlyList<ContentBlock> blocks)
    {
        var text = global::MoonBooks.Core.Content.PlainText.FromBlocks(blocks);
        return new ChapterContent
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Глава" : title.Trim(),
            Blocks = blocks,
            PlainText = text,
            PlainTextLength = text.Length,
        };
    }
}

public sealed class BookContent
{
    public IReadOnlyList<ChapterContent> Chapters { get; init; } = [];
    public IReadOnlyList<TocEntry> Toc { get; init; } = [];
    public IReadOnlyDictionary<string, BookResource> Resources { get; init; } =
        new Dictionary<string, BookResource>(StringComparer.OrdinalIgnoreCase);
    public FootnoteMap Footnotes { get; init; } = new();

    public int TotalChars => Chapters.Sum(chapter => chapter.PlainTextLength);
}
