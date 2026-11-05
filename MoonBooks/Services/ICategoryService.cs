using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Parsing;
using MoonBooks.Data;

namespace MoonBooks.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct);
    Task<Category> CreateAsync(string name, CategoryKind kind, CancellationToken ct);
    Task RenameAsync(long id, string name, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task AssignAsync(long bookId, long categoryId, CancellationToken ct);
    Task UnassignAsync(long bookId, long categoryId, CancellationToken ct);
    Task MergeGenresAsync(long keepId, long dropId, CancellationToken ct);
    Task EnsureFavoritesAsync(CancellationToken ct);
    Task EnsureAutoGenresAsync(BookDbContext db, Book book, BookMetadata metadata, CancellationToken ct);
}
