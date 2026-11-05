using System.Collections.Generic;

namespace MoonBooks.Models;

public class FavouritesModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<BookModel> BookModels { get; set; } = [];
    public int CountBooks { get; set; }
}