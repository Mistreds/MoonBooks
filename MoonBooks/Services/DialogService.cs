using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MoonBooks.Views;

namespace MoonBooks.Services;

public sealed class DialogService : IDialogService
{
    private readonly ICategoryService _categories;
    private readonly ILibraryService _library;

    public DialogService(ICategoryService categories, ILibraryService library)
    {
        _categories = categories;
        _library = library;
    }

    public async Task<IReadOnlyList<string>> PickBooksAsync()
    {
        var owner = Owner;
        if (owner is null)
            return [];
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Добавить книги",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Книги") { Patterns = ["*.fb2", "*.epub"] },
            ],
        });
        return files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToList();
    }

    public async Task<string?> PickFolderAsync()
    {
        var owner = Owner;
        if (owner is null)
            return null;
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка с книгами",
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<ConflictResolution?> ShowConflictAsync(string fileName)
    {
        var owner = Owner;
        if (owner is null)
            return ConflictResolution.Skip;
        var window = ImportConflictWindow.ForFile(fileName);
        await window.ShowDialog(owner);
        return window.Choice;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = Owner;
        if (owner is null)
            return false;
        var window = new ConfirmWindow(title, message, "Удалить", "Отмена");
        await window.ShowDialog(owner);
        return window.Confirmed;
    }

    public async Task ShowCategoriesAsync(long bookId)
    {
        var owner = Owner;
        if (owner is null)
            return;
        var window = new CategoriesWindow(_categories, _library, bookId);
        await window.ShowDialog(owner);
    }

    public async Task<bool> ShowRemoteProgressAsync(string message)
    {
        var owner = Owner;
        if (owner is null)
            return false;
        var window = new ConfirmWindow("Позиция на другом устройстве", message, "Перейти", "Остаться");
        await window.ShowDialog(owner);
        return window.Confirmed;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var owner = Owner;
        if (owner is null)
            return;
        var window = new ConfirmWindow(title, message, "Хорошо", null);
        await window.ShowDialog(owner);
    }

    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
