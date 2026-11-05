using MoonBooks.Core.Content;

namespace MoonBooks.Tests;

public class PlainTextTests
{
    [Fact]
    public void Joins_blocks_with_single_newline_and_trims_trailing_whitespace()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock([new TextRun("Первая строка  ")]),
            new HeadingBlock(2, [new TextRun("Заголовок", InlineStyle.Strong)]),
            new EmptyLineBlock(),
            new ParagraphBlock([
                new TextRun("До "),
                new TextRun("выделения", InlineStyle.Emphasis),
                new TextRun(" и сноски", FootnoteId: "n1"),
            ]),
        };

        var text = PlainText.FromBlocks(blocks);

        Assert.Equal("Первая строка\nЗаголовок\n\nДо выделения и сноски", text);
        Assert.Equal(text.Length, PlainText.Measure(blocks));
    }

    [Fact]
    public void Nested_epigraph_and_poem_flatten_inside_one_slot()
    {
        var blocks = new ContentBlock[]
        {
            new EpigraphBlock(
                [new ParagraphBlock([new TextRun("Эпиграф")])],
                "Автор"),
            new PoemBlock([
                new ParagraphBlock([new TextRun("Строка 1")]),
                new ParagraphBlock([new TextRun("Строка 2")]),
            ]),
        };

        Assert.Equal("Эпиграф\nАвтор\nСтрока 1\nСтрока 2", PlainText.FromBlocks(blocks));
    }
}
