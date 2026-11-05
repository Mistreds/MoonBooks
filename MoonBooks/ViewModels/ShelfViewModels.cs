using System.Collections.ObjectModel;

namespace MoonBooks.ViewModels;

public sealed class ShelfFilterViewModel : ViewModelBase
{
    public required string Name { get; init; }
    public required string Key { get; init; }
    public long? CategoryId { get; init; }
    public int Count { get; init; }
}

public sealed class ShelfRowViewModel : ViewModelBase
{
    public required string Name { get; init; }
    public required string Key { get; init; }
    public long? CategoryId { get; init; }
    public ObservableCollection<BookCardViewModel> Books { get; } = [];
    public bool HasMore { get; set; }
}
