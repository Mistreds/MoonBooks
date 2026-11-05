using MoonBooks.Core.Content;

namespace MoonBooks.Reading;

public sealed class PageSlice
{
    public int ChapterIndex { get; init; }
    public int StartCharOffset { get; init; }
    public int EndCharOffset { get; init; }
    public IReadOnlyList<PageFragment> Fragments { get; init; } = [];
}

public sealed class PageFragment
{
    public required ContentBlock Source { get; init; }
    public required string Text { get; init; }
    public int StartCharOffset { get; init; }
    public int EndCharOffset { get; init; }
    public bool Partial { get; init; }
}

public sealed class BlockSpan
{
    public required ContentBlock Block { get; init; }
    public required string Text { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
}
