using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Data;
using MoonBooks.Services;
using MoonBooks.ViewModels;
using MoonBooks.Views;

namespace MoonBooks;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        MigrateDatabase();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ILibraryService libraryService = new MockLibraryService();
            ISyncService syncService = new MockSyncService();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(libraryService, syncService),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void MigrateDatabase()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoonBooks");
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "moonbooks.db")}")
            .Options;
        using var db = new BookDbContext(options);
        db.Database.Migrate();
    }
}