using System.Collections.Generic;
using MoonBooks.Models;

namespace MoonBooks.Services;

public interface ILibraryService
{
    IReadOnlyList<BookItem> GetBooks();
    IReadOnlyList<ShelfItem> GetShelves();
}

public interface ISyncService
{
    SyncState State { get; }
    void BeginSync();
    void CompleteSync();
    void FailSync();
}
