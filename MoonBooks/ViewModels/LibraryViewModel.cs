using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Domain;
using MoonBooks.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace MoonBooks.ViewModels;

public partial class LibraryViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly IBookImportService _import;
    private readonly IDialogService _dialogs;
    private readonly ICategoryService _categories;
    private readonly ICoverCache _covers;
    private readonly ISettingsService _settings;
    private Func<long, Task> _openBook = _ => Task.CompletedTask;
    private int _totalBooks;

    public LibraryViewModel(
        ILibraryService library,
        IBookImportService import,
        IDialogService dialogs,
        ICategoryService categories,
        ICoverCache covers,
        ISettingsService settings)
    {
        _library = library;
        _import = import;
        _dialogs = dialogs;
        _categories = categories;
        _covers = covers;
        _settings = settings;
        ViewMode = settings.Current.ViewMode;
        ImportFilesCommand = ReactiveCommand.CreateFromTask(ImportFilesAsync);
        ImportFolderCommand = ReactiveCommand.CreateFromTask(ImportFolderAsync);
        SetViewModeCommand = ReactiveCommand.CreateFromTask<LibraryViewMode>(SetViewModeAsync);
        SelectShelfCommand = ReactiveCommand.CreateFromTask<ShelfFilterViewModel>(SelectShelfAsync);
        ShowAllCommand = ReactiveCommand.CreateFromTask<ShelfRowViewModel>(ShowAllAsync);

        this.WhenAnyValue(view => view.SearchText)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(250))
            .ObserveOn(ReactiveUI.RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await ReloadAsync());

        _library.Changes
            .Throttle(TimeSpan.FromMilliseconds(200))
            .ObserveOn(ReactiveUI.RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await ReloadAsync());
    }

    [Reactive] private string _searchText = string.Empty;
    [Reactive] private bool _isLibraryEmpty = true;
    [Reactive] private bool _isSearchMiss;
    [Reactive] private bool _isDropTarget;
    [Reactive] private string _summary = "Библиотека пуста";
    [Reactive] private LibraryViewMode _viewMode;
    [Reactive] private int _booksPerRow = 5;
    [Reactive] private string _selectedShelf = "Все книги";
    [Reactive] private string _status = string.Empty;

    public ObservableCollection<BookCardViewModel> Books { get; } = [];
    public ObservableCollection<BookCardViewModel> ContinueReading { get; } = [];
    public ObservableCollection<ShelfFilterViewModel> Shelves { get; } = [];
    public ObservableCollection<ShelfRowViewModel> ShelfRows { get; } = [];
    public bool ShowContinue => ContinueReading.Count > 0 && string.IsNullOrWhiteSpace(SearchText);
    public bool IsShelves => ViewMode == LibraryViewMode.Shelves;
    public bool IsGrid => ViewMode == LibraryViewMode.Grid;
    public bool IsList => ViewMode == LibraryViewMode.List;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ImportFilesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ImportFolderCommand { get; }
    public ReactiveCommand<LibraryViewMode, System.Reactive.Unit> SetViewModeCommand { get; }
    public ReactiveCommand<ShelfFilterViewModel, System.Reactive.Unit> SelectShelfCommand { get; }
    public ReactiveCommand<ShelfRowViewModel, System.Reactive.Unit> ShowAllCommand { get; }

    public void Attach(Func<long, Task> openBook) => _openBook = openBook;

    public async Task LoadAsync() => await ReloadAsync();

    public void SetBooksPerRow(double width)
    {
        var next = Math.Max(1, (int)Math.Floor((width - 32) / (148 + 22)));
        if (next != BooksPerRow)
        {
            BooksPerRow = next;
            _ = ReloadAsync();
        }
    }

    public async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        var batch = await _import.ImportAsync(paths, null, CancellationToken.None);
        await ResolveAsync(batch);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var all = await _library.QueryAsync(new LibraryQuery(), CancellationToken.None);
        _totalBooks = all.Count;
        var search = SearchText?.Trim();
        IReadOnlyList<Book> filtered = all;
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = all.Where(book =>
                book.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                book.Authors.Any(link => link.Author.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (book.SeriesName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        var selected = Shelves.FirstOrDefault(shelf => shelf.Name == SelectedShelf);
        if (selected?.Key == "reading")
            filtered = filtered.Where(book => book.ReadingState?.Status == BookStatus.Reading).ToList();
        else if (selected?.Key == "finished")
            filtered = filtered.Where(book => book.ReadingState?.Status == BookStatus.Finished).ToList();
        else if (selected?.Key == "favorites")
            filtered = filtered.Where(book => book.Categories.Any(link => link.Category.IsSystem && link.Category.Name == Category.FavoritesName)).ToList();
        else if (selected?.CategoryId is long categoryId)
            filtered = filtered.Where(book => book.Categories.Any(link => link.CategoryId == categoryId)).ToList();

        Replace(Books, filtered.Select(Card));
        var reading = all.Where(book => book.ReadingState?.Status == BookStatus.Reading)
            .OrderByDescending(book => book.ReadingState!.UpdatedAtUtc)
            .Take(8)
            .Select(Card)
            .ToList();
        Replace(ContinueReading, reading);
        BuildShelves(all);
        BuildRows(all);
        IsLibraryEmpty = _totalBooks == 0;
        IsSearchMiss = _totalBooks > 0 && Books.Count == 0;
        Summary = _totalBooks == 0 ? "Пока нет книг" : $"{_totalBooks} книг";
        this.RaisePropertyChanged(nameof(ShowContinue));
        this.RaisePropertyChanged(nameof(IsShelves));
        this.RaisePropertyChanged(nameof(IsGrid));
        this.RaisePropertyChanged(nameof(IsList));
    }

    private void BuildShelves(IReadOnlyList<Book> all)
    {
        var items = new List<ShelfFilterViewModel>
        {
            new() { Name = "Все книги", Key = "all", Count = all.Count },
            new() { Name = "Читаю", Key = "reading", Count = all.Count(book => book.ReadingState?.Status == BookStatus.Reading) },
            new() { Name = "Прочитано", Key = "finished", Count = all.Count(book => book.ReadingState?.Status == BookStatus.Finished) },
            new() { Name = "Избранное", Key = "favorites", Count = all.Count(book => book.Categories.Any(link => link.Category.IsSystem && link.Category.Name == Category.FavoritesName)) },
        };
        foreach (var category in all.SelectMany(book => book.Categories).Select(link => link.Category).DistinctBy(category => category.Id).OrderBy(category => category.Name))
        {
            if (category.IsSystem)
                continue;
            items.Add(new ShelfFilterViewModel
            {
                Name = category.Name,
                Key = "category",
                CategoryId = category.Id,
                Count = all.Count(book => book.Categories.Any(link => link.CategoryId == category.Id)),
            });
        }

        Shelves.Clear();
        foreach (var item in items)
            Shelves.Add(item);
    }

    private void BuildRows(IReadOnlyList<Book> all)
    {
        ShelfRows.Clear();
        void Add(string name, string key, long? categoryId, IEnumerable<Book> books)
        {
            var list = books.ToList();
            if (list.Count == 0)
                return;
            var row = new ShelfRowViewModel { Name = name, Key = key, CategoryId = categoryId };
            var take = Math.Max(1, BooksPerRow * 2);
            foreach (var card in list.Take(take).Select(Card))
                row.Books.Add(card);
            row.HasMore = list.Count > take;
            ShelfRows.Add(row);
        }

        Add("Читаю", "reading", null, all.Where(book => book.ReadingState?.Status == BookStatus.Reading));
        Add("Прочитано", "finished", null, all.Where(book => book.ReadingState?.Status == BookStatus.Finished));
        Add("Избранное", "favorites", null, all.Where(book => book.Categories.Any(link => link.Category.IsSystem && link.Category.Name == Category.FavoritesName)));
        foreach (var category in all.SelectMany(book => book.Categories).Select(link => link.Category).Where(category => !category.IsSystem).DistinctBy(category => category.Id).OrderBy(category => category.Kind).ThenBy(category => category.Name))
        {
            Add(category.Name, "category", category.Id, all.Where(book => book.Categories.Any(link => link.CategoryId == category.Id)));
        }
    }

    private BookCardViewModel Card(Book book) => new(
        book,
        _covers.GetPath(book.CoverCacheKey),
        card => _openBook(card.Id),
        async card => await _dialogs.ShowCategoriesAsync(card.Id),
        ToggleFavoriteAsync,
        DeleteAsync);

    private async Task ToggleFavoriteAsync(BookCardViewModel card)
    {
        var categories = await _categories.ListAsync(CancellationToken.None);
        var favorites = categories.FirstOrDefault(category => category.IsSystem && category.Name == Category.FavoritesName);
        if (favorites is null)
        {
            await _categories.EnsureFavoritesAsync(CancellationToken.None);
            categories = await _categories.ListAsync(CancellationToken.None);
            favorites = categories.First(category => category.IsSystem && category.Name == Category.FavoritesName);
        }

        var book = await _library.GetBookAsync(card.Id, CancellationToken.None);
        var linked = book?.Categories.Any(link => link.CategoryId == favorites.Id) == true;
        if (linked)
            await _categories.UnassignAsync(card.Id, favorites.Id, CancellationToken.None);
        else
            await _categories.AssignAsync(card.Id, favorites.Id, CancellationToken.None);
        await ReloadAsync();
    }

    private async Task DeleteAsync(BookCardViewModel card)
    {
        var deleteFile = await _dialogs.ConfirmAsync("Удалить книгу", $"Удалить «{card.Title}» вместе с файлом?");
        await _library.DeleteBookAsync(card.Id, deleteFile, CancellationToken.None);
        await ReloadAsync();
    }

    private async Task ImportFilesAsync()
    {
        var paths = await _dialogs.PickBooksAsync();
        await ImportPathsAsync(paths);
    }

    private async Task ImportFolderAsync()
    {
        var folder = await _dialogs.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
            return;
        var batch = await _import.ImportFolderAsync(folder, null, CancellationToken.None);
        await ResolveAsync(batch);
        await ReloadAsync();
    }

    private async Task ResolveAsync(ImportBatchResult batch)
    {
        foreach (var item in batch.Items.Where(entry => entry.Status == ImportItemStatus.Conflict && entry.Conflict is not null))
        {
            var choice = await _dialogs.ShowConflictAsync(item.Conflict!.FileName);
            if (choice is null || choice == ConflictResolution.Skip)
                continue;
            var resolved = await _import.ResolveConflictAsync(item.Conflict, choice.Value, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(resolved.Message))
                Status = resolved.Message;
        }

        var failed = batch.Items.Count(item => item.Status == ImportItemStatus.Failed);
        var imported = batch.Items.Count(item => item.Status == ImportItemStatus.Imported);
        if (imported > 0 || failed > 0)
            Status = failed == 0 ? $"Добавлено: {imported}" : $"Добавлено: {imported}, ошибок: {failed}";
    }

    private async Task SetViewModeAsync(LibraryViewMode mode)
    {
        ViewMode = mode;
        await _settings.UpdateAsync(settings => settings.ViewMode = mode);
        this.RaisePropertyChanged(nameof(IsShelves));
        this.RaisePropertyChanged(nameof(IsGrid));
        this.RaisePropertyChanged(nameof(IsList));
    }

    private async Task SelectShelfAsync(ShelfFilterViewModel shelf)
    {
        SelectedShelf = shelf.Name;
        if (ViewMode == LibraryViewMode.Shelves)
            await SetViewModeAsync(LibraryViewMode.Grid);
        await ReloadAsync();
    }

    private async Task ShowAllAsync(ShelfRowViewModel row)
    {
        SelectedShelf = row.Name;
        await SetViewModeAsync(LibraryViewMode.Grid);
        await ReloadAsync();
    }

    private static void Replace(ObservableCollection<BookCardViewModel> target, IEnumerable<BookCardViewModel> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
