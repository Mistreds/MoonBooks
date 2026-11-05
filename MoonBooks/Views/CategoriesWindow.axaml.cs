using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MoonBooks.Core.Domain;
using MoonBooks.Services;

namespace MoonBooks.Views;

public partial class CategoriesWindow : Window
{
    private readonly ICategoryService _categories;
    private readonly ILibraryService _library;
    private readonly long _bookId;

    public CategoriesWindow()
    {
        InitializeComponent();
        _categories = null!;
        _library = null!;
    }

    public CategoriesWindow(ICategoryService categories, ILibraryService library, long bookId)
        : this()
    {
        _categories = categories;
        _library = library;
        _bookId = bookId;
        Opened += async (_, _) => await ReloadAsync();
    }

    private async System.Threading.Tasks.Task ReloadAsync()
    {
        var all = await _categories.ListAsync(default);
        var book = await _library.GetBookAsync(_bookId, default);
        var assigned = book?.Categories.Select(link => link.CategoryId).ToHashSet() ?? [];
        CategoryList.ItemsSource = all.Select(category => new CategoryRow(category, assigned.Contains(category.Id))).ToList();
        MergeKeep.ItemsSource = all.Where(category => category.Kind == CategoryKind.Genre).ToList();
        MergeDrop.ItemsSource = all.Where(category => category.Kind == CategoryKind.Genre).ToList();
    }

    private async void Toggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not CategoryRow row)
            return;
        if (box.IsChecked == true)
            await _categories.AssignAsync(_bookId, row.Id, default);
        else
            await _categories.UnassignAsync(_bookId, row.Id, default);
    }

    private async void Create_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = NewName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        var created = await _categories.CreateAsync(name, CategoryKind.Collection, default);
        await _categories.AssignAsync(_bookId, created.Id, default);
        NewName.Text = string.Empty;
        await ReloadAsync();
    }

    private async void Rename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not CategoryRow row || row.IsSystem)
            return;
        var name = NewName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        await _categories.RenameAsync(row.Id, name, default);
        await ReloadAsync();
    }

    private async void Delete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not CategoryRow row || row.IsSystem)
            return;
        await _categories.DeleteAsync(row.Id, default);
        await ReloadAsync();
    }

    private async void Merge_OnClick(object? sender, RoutedEventArgs e)
    {
        if (MergeKeep.SelectedItem is not Category keep || MergeDrop.SelectedItem is not Category drop)
            return;
        await _categories.MergeGenresAsync(keep.Id, drop.Id, default);
        await ReloadAsync();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();

    private sealed record CategoryRow(long Id, string Name, bool IsSystem, bool Assigned)
    {
        public CategoryRow(Category category, bool assigned)
            : this(category.Id, category.Name, category.IsSystem, assigned)
        {
        }
    }
}
