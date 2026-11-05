using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using MoonBooks.Core.Content;
using MoonBooks.Services;

namespace MoonBooks.Reading;

public sealed class PageLayoutRequest
{
    public required ReaderSettings Settings { get; init; }
    public double ColumnWidth { get; init; }
    public double PageHeight { get; init; }
    public IReadOnlyDictionary<string, byte[]> Images { get; init; } = new Dictionary<string, byte[]>();
}

/// <summary>
/// Packs chapter blocks into pages using Avalonia TextLayout.
/// A page is a <see cref="PageSlice"/> of character offsets, never a stored page number.
/// </summary>
public sealed class PageLayoutEngine
{
    public IReadOnlyList<PageSlice> LayoutChapter(int chapterIndex, ChapterContent chapter, PageLayoutRequest request)
    {
        var spans = Spans(chapter);
        var pages = new List<PageSlice>();
        var fragments = new List<PageFragment>();
        double used = 0;
        int pageStart = spans.Count == 0 ? 0 : spans[0].Start;

        void Close()
        {
            if (fragments.Count == 0)
                return;
            pages.Add(new PageSlice
            {
                ChapterIndex = chapterIndex,
                StartCharOffset = pageStart,
                EndCharOffset = fragments[^1].EndCharOffset,
                Fragments = fragments.ToList(),
            });
            fragments.Clear();
            used = 0;
        }

        foreach (var span in spans)
        {
            if (span.Block is ImageBlock image)
            {
                var height = ImageHeight(image, request);
                var large = height > request.PageHeight * 0.65;
                if (large || used + height > request.PageHeight && fragments.Count > 0)
                    Close();
                pageStart = fragments.Count == 0 ? span.Start : pageStart;
                fragments.Add(new PageFragment
                {
                    Source = span.Block,
                    Text = string.Empty,
                    StartCharOffset = span.Start,
                    EndCharOffset = span.End,
                });
                used += height;
                if (large)
                    Close();
                continue;
            }

            var display = DisplayText(span, request.Settings);
            var fullHeight = Measure(display, request, span.Block);
            if (used + fullHeight <= request.PageHeight || fragments.Count == 0 && fullHeight <= request.PageHeight)
            {
                pageStart = fragments.Count == 0 ? span.Start : pageStart;
                fragments.Add(Fragment(span, display, span.Start, span.End, partial: false));
                used += fullHeight + Spacing(request);
                continue;
            }

            if (fragments.Count > 0)
                Close();

            var remaining = span.Text;
            var offset = span.Start;
            while (remaining.Length > 0)
            {
                var fit = Math.Clamp(FitLength(remaining, request, span.Block), 1, remaining.Length);
                var piece = remaining[..fit];
                var shown = DisplayText(piece, span.Block, request.Settings, partial: true);
                pageStart = fragments.Count == 0 ? offset : pageStart;
                fragments.Add(Fragment(span, shown, offset, offset + piece.Length, partial: true));
                Close();
                var rest = remaining[fit..];
                var trimmed = rest.TrimStart();
                offset += piece.Length + (rest.Length - trimmed.Length);
                remaining = trimmed;
            }
        }

        Close();
        if (pages.Count == 0)
        {
            pages.Add(new PageSlice
            {
                ChapterIndex = chapterIndex,
                StartCharOffset = 0,
                EndCharOffset = 0,
                Fragments = [],
            });
        }

        return pages;
    }

    public static int PageIndexFor(IReadOnlyList<PageSlice> pages, int charOffset)
    {
        if (pages.Count == 0)
            return 0;
        for (var i = 0; i < pages.Count; i++)
        {
            if (charOffset < pages[i].EndCharOffset || i == pages.Count - 1)
                return i;
        }

        return 0;
    }

    private static List<BlockSpan> Spans(ChapterContent chapter)
    {
        var spans = new List<BlockSpan>();
        var cursor = 0;
        for (var i = 0; i < chapter.Blocks.Count; i++)
        {
            var text = chapter.Blocks[i].ToPlainText();
            spans.Add(new BlockSpan
            {
                Block = chapter.Blocks[i],
                Text = text,
                Start = cursor,
                End = cursor + text.Length,
            });
            cursor += text.Length;
            if (i < chapter.Blocks.Count - 1)
                cursor++;
        }

        return spans;
    }

    private static PageFragment Fragment(BlockSpan span, string display, int start, int end, bool partial) => new()
    {
        Source = span.Block,
        Text = display,
        StartCharOffset = start,
        EndCharOffset = end,
        Partial = partial,
    };

    private static string DisplayText(BlockSpan span, ReaderSettings settings) =>
        DisplayText(span.Text, span.Block, settings, partial: false);

    private static string DisplayText(string text, ContentBlock block, ReaderSettings settings, bool partial)
    {
        if (block is HeadingBlock)
            return text;
        if (block is not ParagraphBlock || partial)
            return text;
        return Typography.PrepareParagraph(text, settings.Hyphenation, indent: true);
    }

    private static double Spacing(PageLayoutRequest request) => request.Settings.FontSize * 0.65;

    private static int FitLength(string text, PageLayoutRequest request, ContentBlock block)
    {
        if (Measure(text, request, block) <= request.PageHeight)
            return text.Length;
        var low = 1;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var cut = CutAtWord(text, mid);
            if (Measure(text[..cut], request, block) <= request.PageHeight)
                low = cut;
            else
                high = Math.Max(1, cut - 1);
            if (high < low)
                break;
        }

        return Math.Max(1, CutAtWord(text, low));
    }

    private static int CutAtWord(string text, int length)
    {
        length = Math.Clamp(length, 1, text.Length);
        if (length == text.Length)
            return length;
        var space = text.LastIndexOf(' ', length - 1);
        return space > 8 ? space + 1 : length;
    }

    private static double Measure(string text, PageLayoutRequest request, ContentBlock block)
    {
        if (string.IsNullOrEmpty(text))
            return request.Settings.FontSize;
        var fontSize = block is HeadingBlock ? request.Settings.FontSize * 1.35 : request.Settings.FontSize;
        var alignment = request.Settings.Alignment == ReaderAlignment.Justify ? TextAlignment.Justify : TextAlignment.Left;
        var typeface = new Typeface(Typography.ResolveFont(request.Settings.FontFamily));
        var layout = new TextLayout(
            text,
            typeface,
            fontSize,
            Brushes.Black,
            alignment,
            TextWrapping.Wrap,
            TextTrimming.None,
            textDecorations: null,
            FlowDirection.LeftToRight,
            request.ColumnWidth,
            double.PositiveInfinity,
            fontSize * request.Settings.LineHeightFactor);
        var height = layout.Height;
        if (double.IsNaN(height) || height <= 0)
            height = Estimate(text, request.ColumnWidth, fontSize, request.Settings.LineHeightFactor);
        return height;
    }

    private static double Estimate(string text, double width, double fontSize, double lineFactor)
    {
        var charsPerLine = Math.Max(8, width / (fontSize * 0.5));
        var lines = Math.Max(1, Math.Ceiling(text.Length / charsPerLine));
        return lines * fontSize * lineFactor;
    }

    private static double ImageHeight(ImageBlock image, PageLayoutRequest request)
    {
        if (!request.Images.TryGetValue(image.ResourceId, out var bytes))
            return request.PageHeight * 0.4;
        try
        {
            using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
            if (bitmap is null || bitmap.Width == 0)
                return request.PageHeight * 0.4;
            return request.ColumnWidth * bitmap.Height / bitmap.Width;
        }
        catch (Exception)
        {
            return request.PageHeight * 0.4;
        }
    }
}
