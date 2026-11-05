using MoonBooks.Core.Content;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Reading;

namespace MoonBooks.Core.Sync;

/// <summary>
/// Maps Moon+ chapter/split/offset onto <see cref="BookPosition"/> using <see cref="PlainText"/>.
/// Split length is a hypothesis and must be checked on a device; splitIndex 0 means the
/// position is a plain-text character offset inside the chapter.
/// </summary>
public interface IMoonPlusPositionMapper
{
    BookPosition ToBookPosition(MoonPlusPosition remote, Book book, BookContent? content);
    MoonPlusPosition ToMoonPlus(BookPosition local, Book book, BookContent? content, long timestampUnixMs);
}

public sealed class MoonPlusPositionMapper : IMoonPlusPositionMapper
{
    /// <summary>
    /// Assumed characters per Moon+ split. Not verified against Moon+ Reader.
    /// Used only when <c>splitIndex</c> is not zero. See docs/moonplus-format.md.
    /// </summary>
    public const int AssumedSplitLength = 10_000;

    public BookPosition ToBookPosition(MoonPlusPosition remote, Book book, BookContent? content)
    {
        if (content is null || content.Chapters.Count == 0)
        {
            var knownChapters = content?.Chapters.Count ?? book.ChapterCount;
            if (remote.SplitIndex == 0 && (knownChapters <= 0 || remote.ChapterIndex < knownChapters))
                return new BookPosition(Math.Max(0, remote.ChapterIndex), Math.Max(0, remote.PositionInChapter));
            return FromPercent(remote.Percent, content?.TotalChars ?? book.TotalChars, content);
        }

        if (remote.ChapterIndex < 0 || remote.ChapterIndex >= content.Chapters.Count)
            return FromPercent(remote.Percent, content.TotalChars, content);

        var chapter = content.Chapters[remote.ChapterIndex];
        var offset = remote.SplitIndex == 0
            ? remote.PositionInChapter
            : remote.SplitIndex * AssumedSplitLength + remote.PositionInChapter;
        if (offset < 0)
            offset = 0;
        if (chapter.PlainTextLength > 0 && offset > chapter.PlainTextLength)
            return FromPercent(remote.Percent, content.TotalChars, content);
        if (chapter.PlainTextLength == 0)
            offset = 0;
        return new BookPosition(remote.ChapterIndex, offset);
    }

    public MoonPlusPosition ToMoonPlus(BookPosition local, Book book, BookContent? content, long timestampUnixMs)
    {
        var total = content?.TotalChars ?? book.TotalChars;
        var chapterCount = content?.Chapters.Count ?? book.ChapterCount;
        var chapterIndex = chapterCount == 0 ? 0 : Math.Clamp(local.ChapterIndex, 0, Math.Max(0, chapterCount - 1));
        var chapterLength = content is not null && chapterIndex < content.Chapters.Count
            ? content.Chapters[chapterIndex].PlainTextLength
            : local.CharOffset;
        var offset = Math.Max(0, local.CharOffset);
        if (chapterLength > 0 && offset > chapterLength)
            offset = chapterLength;

        var charsBefore = 0;
        if (content is not null)
        {
            for (var i = 0; i < chapterIndex && i < content.Chapters.Count; i++)
                charsBefore += content.Chapters[i].PlainTextLength;
        }

        var percent = total <= 0 ? 0 : Math.Clamp((charsBefore + offset) * 100d / total, 0, 100);
        return new MoonPlusPosition(timestampUnixMs, chapterIndex, 0, offset, percent);
    }

    private static BookPosition FromPercent(double percent, int totalChars, BookContent? content)
    {
        if (content is null || content.Chapters.Count == 0 || totalChars <= 0)
            return BookPosition.Start;

        var target = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100d * totalChars);
        var cursor = 0;
        for (var i = 0; i < content.Chapters.Count; i++)
        {
            var length = content.Chapters[i].PlainTextLength;
            if (cursor + length >= target || i == content.Chapters.Count - 1)
                return new BookPosition(i, Math.Clamp(target - cursor, 0, length));
            cursor += length;
        }

        return BookPosition.Start;
    }
}
