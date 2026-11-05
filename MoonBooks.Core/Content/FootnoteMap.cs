namespace MoonBooks.Core.Content;

public sealed record Footnote(string Id, string Title, IReadOnlyList<ContentBlock> Blocks);

public sealed class FootnoteMap
{
    private readonly Dictionary<string, Footnote> _notes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Footnote> Notes => _notes;

    public void Add(string id, string? title, IReadOnlyList<ContentBlock> blocks)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        _notes[id] = new Footnote(id, title ?? string.Empty, blocks);
    }

    public bool TryGet(string id, out Footnote note) => _notes.TryGetValue(id, out note!);
}
