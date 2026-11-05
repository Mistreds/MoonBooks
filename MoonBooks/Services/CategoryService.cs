using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Parsing;
using MoonBooks.Data;

namespace MoonBooks.Services;

public sealed class CategoryService : ICategoryService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public CategoryService(IDbContextFactory<BookDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking().OrderBy(category => category.Kind).ThenBy(category => category.SortOrder).ThenBy(category => category.Name).ToListAsync(ct);
    }

    public async Task<Category> CreateAsync(string name, CategoryKind kind, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Введите название.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Categories.FirstOrDefaultAsync(category => category.Kind == kind && category.Name == trimmed, ct);
        if (existing is not null)
            return existing;
        var created = new Category { Name = trimmed, Kind = kind };
        db.Categories.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task RenameAsync(long id, string name, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.SingleAsync(item => item.Id == id, ct);
        if (category.IsSystem)
            throw new InvalidOperationException("Системную полку нельзя переименовать.");
        category.Name = name.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.SingleAsync(item => item.Id == id, ct);
        if (category.IsSystem)
            throw new InvalidOperationException("Системную полку нельзя удалить.");
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignAsync(long bookId, long categoryId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var exists = await db.BookCategories.AnyAsync(link => link.BookId == bookId && link.CategoryId == categoryId, ct);
        if (exists)
            return;
        db.BookCategories.Add(new BookCategory
        {
            BookId = bookId,
            CategoryId = categoryId,
            Source = CategorySource.Manual,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task UnassignAsync(long bookId, long categoryId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var link = await db.BookCategories.FirstOrDefaultAsync(item => item.BookId == bookId && item.CategoryId == categoryId, ct);
        if (link is null)
            return;
        db.BookCategories.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    public async Task MergeGenresAsync(long keepId, long dropId, CancellationToken ct)
    {
        if (keepId == dropId)
            return;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var keep = await db.Categories.SingleAsync(item => item.Id == keepId, ct);
        var drop = await db.Categories.SingleAsync(item => item.Id == dropId, ct);
        if (keep.Kind != CategoryKind.Genre || drop.Kind != CategoryKind.Genre)
            throw new InvalidOperationException("Объединять можно только жанры.");
        var links = await db.BookCategories.Where(link => link.CategoryId == dropId).ToListAsync(ct);
        foreach (var link in links)
        {
            var already = await db.BookCategories.AnyAsync(item => item.BookId == link.BookId && item.CategoryId == keepId, ct);
            if (!already)
            {
                db.BookCategories.Add(new BookCategory
                {
                    BookId = link.BookId,
                    CategoryId = keepId,
                    Source = link.Source,
                });
            }
            db.BookCategories.Remove(link);
        }

        if (string.IsNullOrWhiteSpace(keep.SourceCode) && !string.IsNullOrWhiteSpace(drop.SourceCode))
            keep.SourceCode = drop.SourceCode;
        db.Categories.Remove(drop);
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureFavoritesAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var exists = await db.Categories.AnyAsync(category => category.IsSystem && category.Name == Category.FavoritesName, ct);
        if (exists)
            return;
        db.Categories.Add(new Category
        {
            Name = Category.FavoritesName,
            Kind = CategoryKind.Collection,
            IsSystem = true,
            SortOrder = 0,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureAutoGenresAsync(BookDbContext db, Book book, BookMetadata metadata, CancellationToken ct)
    {
        for (var i = 0; i < metadata.Genres.Count; i++)
        {
            var name = metadata.Genres[i].Trim();
            if (name.Length == 0)
                continue;
            var code = i < metadata.GenreCodes.Count ? metadata.GenreCodes[i] : null;
            var category = await db.Categories.FirstOrDefaultAsync(item => item.Kind == CategoryKind.Genre && item.Name == name, ct);
            if (category is null && !string.IsNullOrWhiteSpace(code))
                category = await db.Categories.FirstOrDefaultAsync(item => item.Kind == CategoryKind.Genre && item.SourceCode == code, ct);
            if (category is null)
            {
                category = new Category
                {
                    Name = name,
                    Kind = CategoryKind.Genre,
                    SourceCode = code,
                };
                db.Categories.Add(category);
            }
            else if (string.IsNullOrWhiteSpace(category.SourceCode) && !string.IsNullOrWhiteSpace(code))
            {
                category.SourceCode = code;
            }

            if (book.Categories.All(link => link.Category != category && link.CategoryId != category.Id))
            {
                book.Categories.Add(new BookCategory
                {
                    Category = category,
                    Source = CategorySource.Auto,
                });
            }
        }
    }
}
