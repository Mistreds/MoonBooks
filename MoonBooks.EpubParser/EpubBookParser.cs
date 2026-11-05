using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using MoonBooks.Core.Content;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Parsing;
using VersOne.Epub;

namespace MoonBooks.EpubParser;

public sealed partial class EpubBookParser : IBookParser
{
    public bool CanParse(string path) =>
        path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);

    public async Task<BookMetadata> ReadMetadataAsync(Stream stream, CancellationToken ct)
    {
        var book = await ReadBookAsync(stream, ct);
        return ReadMetadata(book);
    }

    public async Task<BookContent> ReadContentAsync(Stream stream, CancellationToken ct)
    {
        var book = await ReadBookAsync(stream, ct);
        return ReadContent(book);
    }

    private static async Task<EpubBook> ReadBookAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return await EpubReader.ReadBookAsync(buffer);
    }

    private static BookMetadata ReadMetadata(EpubBook book)
    {
        var metadata = book.Schema.Package.Metadata;
        var genres = new List<string>();
        foreach (var subject in metadata.Subjects)
        {
            foreach (var part in subject.Subject.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                genres.Add(TitleCaseFirst(part));
        }

        string? series = null;
        double? seriesIndex = null;
        foreach (var meta in metadata.MetaItems)
        {
            if (string.Equals(meta.Name, "calibre:series", StringComparison.OrdinalIgnoreCase)
                || string.Equals(meta.Property, "belongs-to-collection", StringComparison.OrdinalIgnoreCase))
                series = meta.Content;
            if (string.Equals(meta.Name, "calibre:series_index", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(meta.Content, NumberStyles.Float, CultureInfo.InvariantCulture, out var index))
                seriesIndex = index;
        }

        var description = metadata.Descriptions.FirstOrDefault()?.Description;
        return new BookMetadata
        {
            Title = string.IsNullOrWhiteSpace(book.Title) ? "Без названия" : book.Title.Trim(),
            Authors = book.AuthorList?.Where(author => !string.IsNullOrWhiteSpace(author)).Select(author => author.Trim()).ToList() ?? [],
            Series = series,
            SeriesIndex = seriesIndex,
            Genres = genres,
            Annotation = string.IsNullOrWhiteSpace(description) ? null : StripTags(description),
            Language = metadata.Languages.FirstOrDefault()?.Language,
            CoverBytes = book.CoverImage,
            Format = BookFormat.Epub,
        };
    }

    private static BookContent ReadContent(EpubBook book)
    {
        var resources = new Dictionary<string, BookResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in book.Content.Images.Local)
            resources[image.Key] = new BookResource(image.Key, image.ContentMimeType ?? "image/jpeg", image.Content ?? []);

        var footnotes = new FootnoteMap();
        var chapters = new List<ChapterContent>();
        var titles = new string?[book.ReadingOrder.Count];
        for (var i = 0; i < book.ReadingOrder.Count; i++)
        {
            var file = book.ReadingOrder[i];
            var (blocks, chapterNotes) = ParseHtml(file.Content ?? string.Empty, file.Key, resources);
            foreach (var note in chapterNotes)
                footnotes.Add(note.Id, note.Title, note.Blocks);
            var title = FirstHeading(blocks) ?? Path.GetFileNameWithoutExtension(file.Key);
            titles[i] = title;
            chapters.Add(ChapterContent.Create(title, blocks));
        }

        if (chapters.Count == 0)
            chapters.Add(ChapterContent.Create(book.Title ?? "Книга", []));

        var toc = new List<TocEntry>();
        if (book.Navigation is not null)
            WalkNav(book.Navigation, book.ReadingOrder, toc);

        return new BookContent
        {
            Chapters = chapters,
            Toc = toc,
            Resources = resources,
            Footnotes = footnotes,
        };
    }

    private static void WalkNav(IEnumerable<EpubNavigationItem> items, IReadOnlyList<EpubLocalTextContentFile> spine, List<TocEntry> toc)
    {
        foreach (var item in items)
        {
            if (item.Link is not null)
            {
                var index = IndexOfSpine(spine, item.Link.ContentFilePath ?? item.HtmlContentFile?.Key);
                if (index >= 0)
                    toc.Add(new TocEntry(item.Title ?? $"Глава {index + 1}", index, 0));
            }

            if (item.NestedItems is { Count: > 0 })
                WalkNav(item.NestedItems, spine, toc);
        }
    }

    private static int IndexOfSpine(IReadOnlyList<EpubLocalTextContentFile> spine, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return -1;
        var file = Path.GetFileName(path);
        for (var i = 0; i < spine.Count; i++)
        {
            if (string.Equals(spine[i].Key, path, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(spine[i].Key), file, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static (List<ContentBlock> Blocks, List<Footnote> Notes) ParseHtml(
        string html,
        string chapterKey,
        IReadOnlyDictionary<string, BookResource> resources)
    {
        var blocks = new List<ContentBlock>();
        var notes = new List<Footnote>();
        if (string.IsNullOrWhiteSpace(html))
            return (blocks, notes);

        var context = BrowsingContext.New(Configuration.Default);
        var document = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
        var body = document.Body;
        if (body is null)
            return (blocks, notes);

        foreach (var child in body.Children)
            AppendElement(child, blocks, notes, chapterKey, resources);
        return (blocks, notes);
    }

    private static void AppendElement(
        IElement element,
        List<ContentBlock> blocks,
        List<Footnote> notes,
        string chapterKey,
        IReadOnlyDictionary<string, BookResource> resources)
    {
        if (IsFootnote(element))
        {
            var id = element.Id ?? string.Empty;
            var inner = new List<ContentBlock>();
            foreach (var child in element.Children)
                AppendElement(child, inner, notes, chapterKey, resources);
            if (inner.Count == 0 && !string.IsNullOrWhiteSpace(element.TextContent))
                inner.Add(new ParagraphBlock([new TextRun(element.TextContent.Trim())]));
            notes.Add(new Footnote(id, id, inner));
            return;
        }

        var name = element.LocalName.ToLowerInvariant();
        switch (name)
        {
            case "p":
                blocks.Add(new ParagraphBlock(ReadHtmlInlines(element)));
                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                blocks.Add(new HeadingBlock(name[1] - '0', ReadHtmlInlines(element)));
                break;
            case "img":
                blocks.Add(new ImageBlock(ResolveImage(element.GetAttribute("src"), chapterKey, resources), element.GetAttribute("alt")));
                break;
            case "blockquote":
                var quote = new List<ContentBlock>();
                foreach (var child in element.Children)
                    AppendElement(child, quote, notes, chapterKey, resources);
                blocks.Add(new EpigraphBlock(quote));
                break;
            case "pre":
                var lines = element.TextContent.Replace("\r", "").Split('\n').Select(line => (ContentBlock)new ParagraphBlock([new TextRun(line)])).ToList();
                blocks.Add(new PoemBlock(lines));
                break;
            case "br":
                blocks.Add(new EmptyLineBlock());
                break;
            default:
                foreach (var child in element.Children)
                    AppendElement(child, blocks, notes, chapterKey, resources);
                if (element.Children.Length == 0 && !string.IsNullOrWhiteSpace(element.TextContent) && name is "div" or "span" or "li")
                    blocks.Add(new ParagraphBlock([new TextRun(Collapse(element.TextContent))]));
                break;
        }
    }

    private static List<TextRun> ReadHtmlInlines(IElement element)
    {
        var runs = new List<TextRun>();
        foreach (var node in element.ChildNodes)
        {
            switch (node)
            {
                case IText text:
                    var value = text.Text;
                    if (!string.IsNullOrEmpty(value))
                        runs.Add(new TextRun(NormalizeSpaces(value)));
                    break;
                case IElement child:
                    var style = child.LocalName.ToLowerInvariant() switch
                    {
                        "em" or "i" => InlineStyle.Emphasis,
                        "strong" or "b" => InlineStyle.Strong,
                        _ => InlineStyle.None,
                    };
                    if (IsNoteRef(child))
                    {
                        var href = child.GetAttribute("href") ?? string.Empty;
                        var id = href.Contains('#') ? href[(href.IndexOf('#') + 1)..] : href;
                        var label = Collapse(child.TextContent);
                        runs.Add(new TextRun(string.IsNullOrWhiteSpace(label) ? id : label, style, id));
                    }
                    else if (child.LocalName.Equals("img", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    else
                    {
                        foreach (var nested in ReadHtmlInlines(child))
                            runs.Add(nested with { Style = nested.Style | style });
                    }
                    break;
            }
        }

        return runs;
    }

    private static bool IsFootnote(IElement element)
    {
        var type = EpubType(element);
        return type.Contains("footnote", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("noteref", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNoteRef(IElement element)
    {
        var type = EpubType(element);
        return type.Contains("noteref", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.GetAttribute("role"), "doc-noteref", StringComparison.OrdinalIgnoreCase);
    }

    private static string EpubType(IElement element)
    {
        foreach (var attribute in element.Attributes)
        {
            if (attribute.Name.Equals("epub:type", StringComparison.OrdinalIgnoreCase)
                || attribute.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) && attribute.Name.Contains("type", StringComparison.OrdinalIgnoreCase))
                return attribute.Value;
        }

        return element.GetAttribute("epub:type")
            ?? element.GetAttribute("type")
            ?? string.Empty;
    }

    private static string ResolveImage(string? src, string chapterKey, IReadOnlyDictionary<string, BookResource> resources)
    {
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;
        if (resources.ContainsKey(src))
            return src;
        var file = Path.GetFileName(src);
        var match = resources.Keys.FirstOrDefault(key =>
            key.EndsWith(src, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(key), file, StringComparison.OrdinalIgnoreCase));
        return match ?? src;
    }

    private static string? FirstHeading(IReadOnlyList<ContentBlock> blocks)
    {
        var heading = blocks.OfType<HeadingBlock>().FirstOrDefault();
        if (heading is null)
            return null;
        var text = string.Concat(heading.Inlines.Select(run => run.Text)).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string TitleCaseFirst(string value)
    {
        if (value.Length == 0)
            return value;
        return char.ToUpper(value[0], CultureInfo.CurrentCulture) + value[1..];
    }

    private static string StripTags(string html) => Collapse(TagRegex().Replace(html, " "));

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeSpaces(string value) => value.Replace('\u00A0', ' ');

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
