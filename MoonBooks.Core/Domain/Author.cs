namespace MoonBooks.Core.Domain;

public sealed class Author
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<BookAuthor> Books { get; set; } = [];
}
