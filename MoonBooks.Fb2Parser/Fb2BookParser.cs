using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Fb2.Document;
using Fb2.Document.LoadingOptions;
using Fb2.Document.Models;
using Fb2.Document.Models.Base;
using FbAuthor = Fb2.Document.Models.Author;
using MoonBooks.Core.Content;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Parsing;

namespace MoonBooks.Fb2Parser;

public sealed class Fb2BookParser : IBookParser
{
    static Fb2BookParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool CanParse(string path) =>
        path.EndsWith(".fb2", StringComparison.OrdinalIgnoreCase);

    public async Task<BookMetadata> ReadMetadataAsync(Stream stream, CancellationToken ct)
    {
        var document = await LoadAsync(stream, ct);
        return ReadMetadata(document);
    }

    public async Task<BookContent> ReadContentAsync(Stream stream, CancellationToken ct)
    {
        var document = await LoadAsync(stream, ct);
        return ReadContent(document);
    }

    private static async Task<Fb2Document> LoadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var document = new Fb2Document();
        await document.LoadAsync(buffer, new Fb2StreamLoadingOptions(false, false, false), ct);
        return document;
    }

    private static BookMetadata ReadMetadata(Fb2Document document)
    {
        var titleInfo = document.Title;
        var title = TextOf(titleInfo?.GetFirstChild<BookTitle>()) ?? "Без названия";
        var authors = titleInfo?.GetChildren<FbAuthor>().Select(FormatAuthor).Where(name => name.Length > 0).ToList()
            ?? [];
        var genres = new List<string>();
        var codes = new List<string>();
        if (titleInfo is not null)
        {
            foreach (var genre in titleInfo.GetChildren<BookGenre>())
            {
                var code = genre.Content?.Trim() ?? string.Empty;
                if (code.Length == 0)
                    continue;
                codes.Add(code);
                genres.Add(Fb2GenreMap.ToDisplayName(code));
            }
        }

        var sequence = titleInfo?.GetFirstChild<SequenceInfo>();
        string? series = null;
        double? seriesIndex = null;
        if (sequence is not null && sequence.TryGetAttribute("name", out var seriesAttr) && seriesAttr is not null)
            series = seriesAttr.Value;
        if (sequence is not null && sequence.TryGetAttribute("number", out var numberAttr) && numberAttr is not null
            && double.TryParse(numberAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var index))
            seriesIndex = index;

        var annotation = titleInfo?.GetFirstChild<Annotation>();
        var annotationText = annotation is null ? null : PlainText.FromBlocks(ReadContainerBlocks(annotation, 1));
        var language = titleInfo?.GetFirstChild<Lang>()?.Content;
        var cover = ReadCover(document, titleInfo);

        return new BookMetadata
        {
            Title = title,
            Authors = authors,
            Series = series,
            SeriesIndex = seriesIndex,
            Genres = genres,
            GenreCodes = codes,
            Annotation = string.IsNullOrWhiteSpace(annotationText) ? null : annotationText,
            Language = language,
            CoverBytes = cover,
            Format = BookFormat.Fb2,
        };
    }

    private static BookContent ReadContent(Fb2Document document)
    {
        var resources = new Dictionary<string, BookResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var binary in document.BinaryImages)
        {
            if (!binary.TryGetAttribute("id", out var idAttr) || idAttr is null || string.IsNullOrWhiteSpace(binary.Content))
                continue;
            try
            {
                var bytes = Convert.FromBase64String(StripWhitespace(binary.Content));
                var media = binary.TryGetAttribute("content-type", out var typeAttr) && typeAttr is not null
                    ? typeAttr.Value
                    : "application/octet-stream";
                resources[idAttr.Value] = new BookResource(idAttr.Value, media ?? "application/octet-stream", bytes);
            }
            catch (FormatException)
            {
            }
        }

        var notes = document.Bodies.FirstOrDefault(body => IsNotes(body));
        var footnotes = new FootnoteMap();
        if (notes is not null)
        {
            foreach (var section in notes.GetChildren<BodySection>())
            {
                var id = section.TryGetAttribute("id", out var idAttr) && idAttr is not null ? idAttr.Value : string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                footnotes.Add(id, SectionTitle(section), ReadSectionBlocks(section, nestedHeadings: true));
            }
        }

        var main = document.Bodies.FirstOrDefault(body => !IsNotes(body)) ?? document.Bodies.FirstOrDefault();
        var chapters = new List<ChapterContent>();
        var toc = new List<TocEntry>();
        if (main is not null)
        {
            var sections = main.GetChildren<BodySection>().ToList();
            if (sections.Count == 0)
            {
                var blocks = ReadContainerBlocks(main, 1);
                chapters.Add(ChapterContent.Create(TextOf(document.Title?.GetFirstChild<BookTitle>()) ?? "Книга", blocks));
            }
            else
            {
                for (var i = 0; i < sections.Count; i++)
                {
                    var title = SectionTitle(sections[i]);
                    var blocks = ReadSectionBlocks(sections[i], nestedHeadings: true);
                    chapters.Add(ChapterContent.Create(title, blocks));
                    toc.Add(new TocEntry(title, i, 0));
                }
            }
        }

        if (chapters.Count == 0)
            chapters.Add(ChapterContent.Create("Книга", []));

        return new BookContent
        {
            Chapters = chapters,
            Toc = toc,
            Resources = resources,
            Footnotes = footnotes,
        };
    }

    private static List<ContentBlock> ReadSectionBlocks(BodySection section, bool nestedHeadings)
    {
        var blocks = new List<ContentBlock>();
        foreach (var node in section.Content)
        {
            switch (node)
            {
                case Title:
                    break;
                case BodySection nested:
                    if (nestedHeadings)
                    {
                        var heading = SectionTitle(nested);
                        if (!string.IsNullOrWhiteSpace(heading) && heading != "Глава")
                            blocks.Add(new HeadingBlock(2, [new TextRun(heading)]));
                        blocks.AddRange(ReadSectionBlocks(nested, nestedHeadings: true));
                    }
                    break;
                default:
                    AppendBlock(node, blocks, 2);
                    break;
            }
        }

        return blocks;
    }

    private static List<ContentBlock> ReadContainerBlocks(Fb2Container container, int headingLevel)
    {
        var blocks = new List<ContentBlock>();
        foreach (var node in container.Content)
            AppendBlock(node, blocks, headingLevel);
        return blocks;
    }

    private static void AppendBlock(Fb2Node node, List<ContentBlock> blocks, int headingLevel)
    {
        switch (node)
        {
            case Title:
            case BodySection:
                break;
            case Paragraph paragraph:
                blocks.Add(new ParagraphBlock(ReadInlines(paragraph)));
                break;
            case SubTitle subtitle:
                blocks.Add(new HeadingBlock(headingLevel, ReadInlines(subtitle)));
                break;
            case EmptyLine:
                blocks.Add(new EmptyLineBlock());
                break;
            case Image image:
                blocks.Add(new ImageBlock(ResourceId(image), null));
                break;
            case Epigraph epigraph:
                blocks.Add(new EpigraphBlock(ReadContainerBlocks(epigraph, headingLevel), Attribution(epigraph)));
                break;
            case Quote quote:
                blocks.Add(new EpigraphBlock(ReadContainerBlocks(quote, headingLevel), Attribution(quote)));
                break;
            case Poem poem:
                blocks.Add(new PoemBlock(ReadPoemLines(poem)));
                break;
            case Fb2Container container:
                blocks.AddRange(ReadContainerBlocks(container, headingLevel));
                break;
        }
    }

    private static List<ContentBlock> ReadPoemLines(Poem poem)
    {
        var lines = new List<ContentBlock>();
        foreach (var node in poem.Content)
        {
            switch (node)
            {
                case Stanza stanza:
                    foreach (var line in stanza.Content)
                    {
                        if (line is StanzaVerse or Paragraph)
                            lines.Add(new ParagraphBlock(ReadInlines((Fb2Container)line)));
                    }
                    break;
                case StanzaVerse verse:
                    lines.Add(new ParagraphBlock(ReadInlines(verse)));
                    break;
                case Paragraph paragraph:
                    lines.Add(new ParagraphBlock(ReadInlines(paragraph)));
                    break;
            }
        }

        return lines;
    }

    private static string? Attribution(Fb2Container container)
    {
        var author = container.GetFirstChild<TextAuthor>();
        var text = author is null ? null : InlineString(ReadInlines(author));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static List<TextRun> ReadInlines(Fb2Container container, InlineStyle style = InlineStyle.None)
    {
        var runs = new List<TextRun>();
        foreach (var node in container.Content)
        {
            switch (node)
            {
                case TextItem text when !string.IsNullOrEmpty(text.Content):
                    runs.Add(new TextRun(text.Content, style));
                    break;
                case Emphasis emphasis:
                    runs.AddRange(ReadInlines(emphasis, style | InlineStyle.Emphasis));
                    break;
                case Strong strong:
                    runs.AddRange(ReadInlines(strong, style | InlineStyle.Strong));
                    break;
                case TextLink link:
                    var href = link.TryGetAttribute("href", out var hrefAttr) && hrefAttr is not null ? hrefAttr.Value : null;
                    var noteId = string.IsNullOrWhiteSpace(href) ? null : href.Trim().TrimStart('#');
                    var linked = ReadInlines(link, style);
                    if (linked.Count == 0)
                        runs.Add(new TextRun(noteId ?? string.Empty, style, noteId));
                    else
                    {
                        runs.Add(new TextRun(InlineString(linked), style, noteId));
                    }
                    break;
                case Fb2Container nested:
                    runs.AddRange(ReadInlines(nested, style));
                    break;
                case Fb2Element element when !string.IsNullOrEmpty(element.Content):
                    runs.Add(new TextRun(element.Content, style));
                    break;
            }
        }

        return runs;
    }

    private static string SectionTitle(BodySection section)
    {
        var title = section.GetFirstChild<Title>();
        if (title is null)
            return "Глава";
        var text = InlineString(ReadInlines(title));
        return string.IsNullOrWhiteSpace(text) ? "Глава" : text;
    }

    private static string? TextOf(Fb2Element? element) =>
        string.IsNullOrWhiteSpace(element?.Content) ? null : element.Content.Trim();

    private static string FormatAuthor(FbAuthor author)
    {
        var parts = new[]
        {
            author.GetFirstChild<FirstName>()?.Content,
            author.GetFirstChild<MiddleName>()?.Content,
            author.GetFirstChild<LastName>()?.Content,
        };
        var name = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        if (name.Length > 0)
            return name;
        return author.GetFirstChild<Nickname>()?.Content?.Trim() ?? string.Empty;
    }

    private static bool IsNotes(BookBody body)
    {
        if (!body.TryGetAttribute("name", out var name) || name is null)
            return false;
        return string.Equals(name.Value, "notes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name.Value, "comments", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? ReadCover(Fb2Document document, TitleInfo? titleInfo)
    {
        var image = titleInfo?.GetFirstChild<Coverpage>()?.GetFirstChild<Image>();
        if (image is null)
            return null;
        var id = ResourceId(image);
        var binary = document.BinaryImages.FirstOrDefault(item =>
            item.TryGetAttribute("id", out var attr) && attr is not null && string.Equals(attr.Value, id, StringComparison.OrdinalIgnoreCase));
        if (binary is null || string.IsNullOrWhiteSpace(binary.Content))
            return null;
        try
        {
            return Convert.FromBase64String(StripWhitespace(binary.Content));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string ResourceId(Image image)
    {
        if (!image.TryGetAttribute("href", out var href) || href is null)
            return string.Empty;
        return href.Value.Trim().TrimStart('#');
    }

    private static string InlineString(IReadOnlyList<TextRun> runs) =>
        string.Concat(runs.Select(run => run.Text)).Trim();

    private static string StripWhitespace(string value) =>
        string.Concat(value.Where(ch => !char.IsWhiteSpace(ch)));
}
