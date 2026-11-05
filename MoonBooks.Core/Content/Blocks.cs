namespace MoonBooks.Core.Content;

public abstract class ContentBlock
{
    public abstract string ToPlainText();
}

public sealed class ParagraphBlock : ContentBlock
{
    public ParagraphBlock(IReadOnlyList<TextRun> inlines) => Inlines = inlines;
    public IReadOnlyList<TextRun> Inlines { get; }
    public override string ToPlainText() => PlainText.InlineText(Inlines);
}

public sealed class HeadingBlock : ContentBlock
{
    public HeadingBlock(int level, IReadOnlyList<TextRun> inlines)
    {
        Level = level;
        Inlines = inlines;
    }

    public int Level { get; }
    public IReadOnlyList<TextRun> Inlines { get; }
    public override string ToPlainText() => PlainText.InlineText(Inlines);
}

public sealed class ImageBlock : ContentBlock
{
    public ImageBlock(string resourceId, string? alt = null)
    {
        ResourceId = resourceId;
        Alt = alt;
    }

    public string ResourceId { get; }
    public string? Alt { get; }
    public override string ToPlainText() => string.Empty;
}

public sealed class EpigraphBlock : ContentBlock
{
    public EpigraphBlock(IReadOnlyList<ContentBlock> blocks, string? attribution = null)
    {
        Blocks = blocks;
        Attribution = attribution;
    }

    public IReadOnlyList<ContentBlock> Blocks { get; }
    public string? Attribution { get; }

    public override string ToPlainText()
    {
        var body = PlainText.FromBlocks(Blocks);
        if (string.IsNullOrWhiteSpace(Attribution))
            return body;
        var credit = Attribution.TrimEnd();
        return body.Length == 0 ? credit : body + "\n" + credit;
    }
}

public sealed class PoemBlock : ContentBlock
{
    public PoemBlock(IReadOnlyList<ContentBlock> lines) => Lines = lines;
    public IReadOnlyList<ContentBlock> Lines { get; }
    public override string ToPlainText() => PlainText.FromBlocks(Lines);
}

public sealed class EmptyLineBlock : ContentBlock
{
    public override string ToPlainText() => string.Empty;
}
