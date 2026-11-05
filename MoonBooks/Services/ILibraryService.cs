using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Domain;

namespace MoonBooks.Services;

public enum LibraryChangeKind
{
    Reset,
    Upsert,
    Delete,
}

public sealed record LibraryChanged(LibraryChangeKind Kind, long? BookId);

public sealed class LibraryQuery
{
    public string? Search { get; init; }
    public long? CategoryId { get; init; }
    public BookStatus? Status { get; init; }
    public bool FavoritesOnly { get; init; }
}

public interface ILibraryService
{
    IObservable<LibraryChanged> Changes { get; }
    void Notify(LibraryChanged change);
    Task<IReadOnlyList<Book>> QueryAsync(LibraryQuery query, CancellationToken ct);
    Task<IReadOnlyList<Book>> GetContinueReadingAsync(CancellationToken ct);
    Task<Book?> GetBookAsync(long id, CancellationToken ct);
    Task DeleteBookAsync(long id, bool deleteFile, CancellationToken ct);
    Task SavePositionAsync(long bookId, int chapterIndex, int charOffset, double percent, CancellationToken ct);
}
