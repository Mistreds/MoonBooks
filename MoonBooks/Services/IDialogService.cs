using System.Collections.Generic;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public interface IDialogService
{
    Task<IReadOnlyList<string>> PickBooksAsync();
    Task<string?> PickFolderAsync();
    Task<ConflictResolution?> ShowConflictAsync(string fileName);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowCategoriesAsync(long bookId);
    Task<bool> ShowRemoteProgressAsync(string message);
    Task ShowMessageAsync(string title, string message);
}
