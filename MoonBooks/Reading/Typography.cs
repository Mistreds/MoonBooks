using System;
using System.Collections.Generic;
using NHyphenator;
using NHyphenator.Loaders;

namespace MoonBooks.Reading;

public static class Typography
{
    // NHyphenator inserts U+00AD. Avalonia TextLayout may ignore the break opportunity.
    // Pagination does not depend on the hyphen being honored.
    // The package marks its embedded pattern loader obsolete and points at a file loader.
#pragma warning disable CS0618
    private static readonly Hyphenator Russian = new(new ResourceHyphenatePatternsLoader(HyphenatePatternsLanguage.Russian), "\u00ad", 2, 5, false, false);
#pragma warning restore CS0618

    public static string PrepareParagraph(string text, bool hyphenate, bool indent)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var dialogue = text[0] is '—' or '–' or '-';
        if (dialogue && text.Length > 1 && text[1] == ' ')
            text = string.Concat(text[0], "\u00A0", text[2..]);
        if (indent && !dialogue)
            text = "\u2003\u2003" + text;
        if (!hyphenate)
            return text;

        try
        {
            return Russian.HyphenateText(text);
        }
        catch (Exception)
        {
            return text;
        }
    }

    public static IReadOnlyList<(string Text, bool Bold)> BionicPieces(string text)
    {
        var pieces = new List<(string Text, bool Bold)>();
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsLetter(text[index]))
            {
                var start = index;
                while (index < text.Length && char.IsLetter(text[index]))
                    index++;
                var word = text[start..index];
                var cut = Math.Max(1, (word.Length + 1) / 2);
                if (word.Length == 1)
                    pieces.Add((word, false));
                else
                {
                    pieces.Add((word[..cut], true));
                    pieces.Add((word[cut..], false));
                }
            }
            else
            {
                var start = index;
                while (index < text.Length && !char.IsLetter(text[index]))
                    index++;
                pieces.Add((text[start..index], false));
            }
        }

        return pieces;
    }

    public static string ResolveFont(string? family) => family switch
    {
        "Arial" => "Arial, Inter, sans-serif",
        "Verdana" => "Verdana, Inter, sans-serif",
        "PT Serif" => "PT Serif, Times New Roman, Georgia, serif",
        _ => "Times New Roman, Georgia, serif",
    };
}
