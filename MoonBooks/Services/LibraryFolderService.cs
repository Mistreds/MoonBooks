using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.IO;
using MoonBooks.Data;

namespace MoonBooks.Services;

public sealed class LibraryFolderService : ILibraryFolderService
{
    private readonly ISettingsService _settings;
    private readonly IDbContextFactory<BookDbContext> _dbFactory;

    public LibraryFolderService(ISettingsService settings, IDbContextFactory<BookDbContext> dbFactory)
    {
        _settings = settings;
        _dbFactory = dbFactory;
    }

    public string CurrentPath => _settings.Current.LibraryPath;

    public async Task<FolderMoveResult> ChangeFolderAsync(string newPath, bool moveBooks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPath))
            return new FolderMoveResult(false, "Укажите папку библиотеки.", 0);

        var destination = Path.GetFullPath(newPath);
        var current = Path.GetFullPath(string.IsNullOrWhiteSpace(CurrentPath) ? destination : CurrentPath);
        Directory.CreateDirectory(destination);

        if (string.Equals(destination, current, StringComparison.OrdinalIgnoreCase))
            return new FolderMoveResult(true, null, 0);

        if (destination.StartsWith(current + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return new FolderMoveResult(false, "Новая папка не может лежать внутри текущей.", 0);

        if (!moveBooks)
        {
            await _settings.UpdateAsync(settings => settings.LibraryPath = destination, ct);
            return new FolderMoveResult(true, null, 0);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var books = await db.Books.AsNoTracking().Where(book => !book.IsMissing).ToListAsync(ct);
        var copied = new List<(string Source, string Dest)>();
        try
        {
            foreach (var book in books)
            {
                ct.ThrowIfCancellationRequested();
                var source = Path.Combine(current, book.FileName);
                if (!File.Exists(source))
                    throw new IOException($"Нет файла «{book.FileName}». Перенос отменён.");

                var dest = Path.Combine(destination, book.FileName);
                var partial = dest + ".partial";
                File.Copy(source, partial, overwrite: true);
                var sourceHash = await FileHashing.Sha256FileAsync(source, ct);
                var copyHash = await FileHashing.Sha256FileAsync(partial, ct);
                if (!string.Equals(sourceHash, copyHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Копия «{book.FileName}» не совпала с оригиналом.");

                File.Move(partial, dest, overwrite: true);
                copied.Add((source, dest));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rollback(copied, destination);
            return new FolderMoveResult(false, ex.Message, 0);
        }

        foreach (var (source, _) in copied)
        {
            if (File.Exists(source))
                File.Delete(source);
        }

        await _settings.UpdateAsync(settings => settings.LibraryPath = destination, ct);
        return new FolderMoveResult(true, null, copied.Count);
    }

    private static void Rollback(List<(string Source, string Dest)> copied, string destination)
    {
        foreach (var (_, dest) in copied)
        {
            if (File.Exists(dest))
                File.Delete(dest);
        }

        if (!Directory.Exists(destination))
            return;

        foreach (var partial in Directory.EnumerateFiles(destination, "*.partial"))
            File.Delete(partial);
    }
}
