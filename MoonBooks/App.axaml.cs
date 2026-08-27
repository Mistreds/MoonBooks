using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
}