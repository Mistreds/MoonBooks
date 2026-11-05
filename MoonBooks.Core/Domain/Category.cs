namespace MoonBooks.Core.Domain;

public sealed class Category
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CategoryKind Kind { get; set; }
    public string? SourceCode { get; set; }
    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
    public List<BookCategory> Books { get; set; } = [];

    public const string FavoritesName = "Избранное";
}
