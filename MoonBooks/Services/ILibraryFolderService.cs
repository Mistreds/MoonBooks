using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public sealed record FolderMoveResult(bool Success, string? Error, int Moved);

public interface ILibraryFolderService
{
    string CurrentPath { get; }
    Task<FolderMoveResult> ChangeFolderAsync(string newPath, bool moveBooks, CancellationToken ct);
}
