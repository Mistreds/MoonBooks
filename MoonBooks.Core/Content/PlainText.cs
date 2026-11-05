namespace MoonBooks.Core.Content;

/// <summary>
/// Single normalization rule for chapter plain text and <see cref="Reading.BookPosition.CharOffset"/>.
/// Moon+ comparison may require adjusting this later; keep every offset calculation here.
///
/// Rule:
/// - Walk blocks in document order.
/// - Each block contributes its visible text with trailing whitespace removed.
/// - Markup is not counted: emphasis, strong and links contribute only their character data.
/// - Blocks are joined with a single '\n'. That newline is part of the normalized string
///   and occupies one character offset. A chapter with a single block has no extra newline.
/// - Nested blocks (epigraph, poem) are flattened with the same rule before the parent is joined.
/// - Images and empty lines contribute empty text but still take a slot in the join,
///   so the separators around them remain stable.
/// </summary>
public static class PlainText
{
    public static string FromBlocks(IReadOnlyList<ContentBlock> blocks)
    {
        if (blocks.Count == 0)
            return string.Empty;

        var parts = new string[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
            parts[i] = blocks[i].ToPlainText();
        return string.Join('\n', parts);
    }

    public static string InlineText(IReadOnlyList<TextRun> inlines)
    {
        if (inlines.Count == 0)
            return string.Empty;

        var builder = new System.Text.StringBuilder();
        foreach (var run in inlines)
        {
            if (!string.IsNullOrEmpty(run.Text))
                builder.Append(run.Text);
        }

        return builder.ToString().TrimEnd();
    }

    public static int Measure(IReadOnlyList<ContentBlock> blocks) => FromBlocks(blocks).Length;
}
