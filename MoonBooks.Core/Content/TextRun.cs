namespace MoonBooks.Core.Content;

[Flags]
public enum InlineStyle
{
    None = 0,
    Emphasis = 1,
    Strong = 2,
}

public sealed record TextRun(string Text, InlineStyle Style = InlineStyle.None, string? FootnoteId = null);
