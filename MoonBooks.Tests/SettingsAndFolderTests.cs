using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;
using MoonBooks.Data;
using MoonBooks.Services;

namespace MoonBooks.Tests;

public class SettingsAndFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moonbooks-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsAndFolderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Settings_roundtrip_preserves_reader_view_mode_and_device()
    {
        var path = Path.Combine(_root, "settings.json");
        var library = Path.Combine(_root, "library");
        var service = new SettingsService(path, library);
        var device = service.Current.DeviceId;

        await service.UpdateAsync(settings =>
        {
            settings.ViewMode = LibraryViewMode.List;
            settings.Reader.FontSize = 22;
            settings.Reader.Theme = ReaderThemeKind.Black;
            settings.Reader.Bionic = true;
            settings.SyncProvider = SyncProviderKind.WebDav;
            settings.WatchLibrary = false;
        });

        var reloaded = new SettingsService(path, library);
        Assert.Equal(device, reloaded.Current.DeviceId);
        Assert.Equal(LibraryViewMode.List, reloaded.Current.ViewMode);
        Assert.Equal(22, reloaded.Current.Reader.FontSize);
        Assert.Equal(ReaderThemeKind.Black, reloaded.Current.Reader.Theme);
        Assert.True(reloaded.Current.Reader.Bionic);
        Assert.False(reloaded.Current.WatchLibrary);
        Assert.Equal(library, reloaded.Current.LibraryPath);
    }

    [Fact]
    public async Task Settings_changes_are_observable()
    {
        var service = new SettingsService(Path.Combine(_root, "s2.json"), Path.Combine(_root, "lib2"));
        LibraryViewMode? seen = null;
        using var sub = service.Changes.Subscribe(settings => seen = settings.ViewMode);
        await service.UpdateAsync(settings => settings.ViewMode = LibraryViewMode.Shelves);
        Assert.Equal(LibraryViewMode.Shelves, seen);
    }

    [Fact]
    public async Task File_secret_store_roundtrip_is_owner_only()
    {
        var store = new FileSecretStore(Path.Combine(_root, "secrets"));
        await store.SetAsync(SecretKeys.WebDavPassword, "p@ss", default);
        Assert.Equal("p@ss", await store.GetAsync(SecretKeys.WebDavPassword, default));

        var file = Path.Combine(_root, "secrets", SecretKeys.WebDavPassword);
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(file);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
            Assert.False(mode.HasFlag(UnixFileMode.GroupRead));
            Assert.False(mode.HasFlag(UnixFileMode.OtherRead));
        }

        await store.RemoveAsync(SecretKeys.WebDavPassword, default);
        Assert.Null(await store.GetAsync(SecretKeys.WebDavPassword, default));
    }

    [Fact]
    public async Task Dpapi_store_refuses_linux()
    {
        if (OperatingSystem.IsWindows())
            return;

#pragma warning disable CA1416
        var store = new DpapiSecretStore(Path.Combine(_root, "dpapi"));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => store.SetAsync("k", "v", default));
#pragma warning restore CA1416
    }

    [Fact]
    public async Task Move_folder_copies_verifies_and_deletes_source()
    {
        var oldPath = Path.Combine(_root, "old");
        var newPath = Path.Combine(_root, "new");
        Directory.CreateDirectory(oldPath);
        var bookPath = Path.Combine(oldPath, "Kept.fb2");
        await File.WriteAllTextAsync(bookPath, "<FictionBook>tale</FictionBook>");

        var (settings, folder) = await CreateFolderServiceAsync(oldPath);
        var result = await folder.ChangeFolderAsync(newPath, moveBooks: true, default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Moved);
        Assert.False(File.Exists(bookPath));
        Assert.Equal("<FictionBook>tale</FictionBook>", await File.ReadAllTextAsync(Path.Combine(newPath, "Kept.fb2")));
        Assert.Equal(Path.GetFullPath(newPath), settings.Current.LibraryPath);
    }

    [Fact]
    public async Task Move_folder_rolls_back_when_a_file_is_missing()
    {
        var oldPath = Path.Combine(_root, "old2");
        var newPath = Path.Combine(_root, "new2");
        Directory.CreateDirectory(oldPath);
        var kept = Path.Combine(oldPath, "Kept.fb2");
        await File.WriteAllTextAsync(kept, "kept");

        var (settings, folder) = await CreateFolderServiceAsync(oldPath, extraMissing: "Gone.fb2");
        var result = await folder.ChangeFolderAsync(newPath, moveBooks: true, default);

        Assert.False(result.Success);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(Path.Combine(newPath, "Kept.fb2")));
        Assert.Equal(Path.GetFullPath(oldPath), settings.Current.LibraryPath);
    }

    private async Task<(SettingsService Settings, LibraryFolderService Folder)> CreateFolderServiceAsync(string library, string? extraMissing = null)
    {
        var dbPath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");
        var options = new DbContextOptionsBuilder<BookDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        await using (var db = new BookDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Books.Add(new Book
            {
                Title = "Kept",
                SortTitle = "kept",
                Format = BookFormat.Fb2,
                FileName = "Kept.fb2",
                FileHash = new string('a', 64),
                AddedAtUtc = DateTime.UtcNow,
            });
            if (extraMissing is not null)
            {
                db.Books.Add(new Book
                {
                    Title = "Gone",
                    SortTitle = "gone",
                    Format = BookFormat.Fb2,
                    FileName = extraMissing,
                    FileHash = new string('b', 64),
                    AddedAtUtc = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync();
        }

        var settings = new SettingsService(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json"), library);
        return (settings, new LibraryFolderService(settings, new TestDbFactory(options)));
    }

    private sealed class TestDbFactory(DbContextOptions<BookDbContext> options) : IDbContextFactory<BookDbContext>
    {
        public BookDbContext CreateDbContext() => new(options);
        public Task<BookDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
