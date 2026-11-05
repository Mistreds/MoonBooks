using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MoonBooks.Core.Parsing;
using MoonBooks.Core.Sync;
using MoonBooks.Data;
using MoonBooks.EpubParser;
using MoonBooks.Fb2Parser;
using MoonBooks.Services;
using MoonBooks.ViewModels;
using MoonBooks.Views;

namespace MoonBooks;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var builder = Host.CreateApplicationBuilder();
        var paths = new AppPaths();
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<ISettingsService>(new SettingsService(paths));
        if (OperatingSystem.IsWindows())
            builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>();
        else
            builder.Services.AddSingleton<ISecretStore, FileSecretStore>();

        builder.Services.AddDbContextFactory<BookDbContext>(options =>
            options.UseSqlite($"Data Source={paths.DatabasePath}"));
        builder.Services.AddSingleton<IBookParser, Fb2BookParser>();
        builder.Services.AddSingleton<IBookParser, EpubBookParser>();
        builder.Services.AddSingleton<ICoverCache, CoverCache>();
        builder.Services.AddSingleton<ICategoryService, CategoryService>();
        builder.Services.AddSingleton<ILibraryFolderService, LibraryFolderService>();
        builder.Services.AddSingleton<IBookImportService, BookImportService>();
        builder.Services.AddSingleton<ILibraryService, LibraryService>();
        builder.Services.AddSingleton<LibraryWatcher>();
        builder.Services.AddSingleton<IMoonPlusPositionMapper, MoonPlusPositionMapper>();
        builder.Services.AddSingleton<ISyncProviderFactory, SyncProviderFactory>();
        builder.Services.AddSingleton<SyncEngine>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<ISyncService, SyncService>();
        builder.Services.AddSingleton<SyncScheduler>();
        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddTransient<ReaderViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();

        _host = builder.Build();
        var settingsView = _host.Services.GetRequiredService<SettingsViewModel>();
        settingsView.AttachSync(_host.Services.GetRequiredService<ISecretStore>(), _host.Services.GetRequiredService<ISyncService>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = _host.Services.GetRequiredService<ShellViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = shell };
            desktop.Exit += async (_, _) =>
            {
                await shell.ShutdownAsync();
                _host.Dispose();
            };
            Dispatcher.UIThread.Post(async () => await shell.InitializeAsync());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
