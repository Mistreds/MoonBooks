namespace MoonBooks.Core.Domain;

public sealed class BookCategory
{
    public long BookId { get; set; }
    public Book Book { get; set; } = null!;
    public long CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public CategorySource Source { get; set; }
}
