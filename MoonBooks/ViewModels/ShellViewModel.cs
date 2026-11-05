using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoonBooks.Core.Domain;
using MoonBooks.Data;
using MoonBooks.Services;
using ReactiveUI;

namespace MoonBooks.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly IDbContextFactory<BookDbContext> _dbFactory;
    private readonly ISettingsService _settings;
    private readonly ICategoryService _categories;
    private readonly LibraryWatcher _watcher;
    private readonly SyncScheduler _scheduler;
    private readonly ISyncService _sync;
    private ViewModelBase? _currentPage;
    private string _syncMessage = "Синхронизация выключена";
    private SyncState _syncState = SyncState.Disabled;
    private ReaderViewModel? _reader;

    public ShellViewModel(
        IServiceProvider services,
        IDbContextFactory<BookDbContext> dbFactory,
        ISettingsService settings,
        ICategoryService categories,
        LibraryViewModel library,
        SettingsViewModel settingsViewModel,
        LibraryWatcher watcher,
        SyncScheduler scheduler,
        ISyncService sync)
    {
        _services = services;
        _dbFactory = dbFactory;
        _settings = settings;
        _categories = categories;
        Library = library;
        Settings = settingsViewModel;
        _watcher = watcher;
        _scheduler = scheduler;
        _sync = sync;
        library.Attach(OpenBookAsync);
        ShowLibraryCommand = ReactiveCommand.CreateFromTask(ShowLibraryAsync);
        ShowSettingsCommand = ReactiveCommand.CreateFromTask(ShowSettingsAsync);
        SyncCommand = ReactiveCommand.CreateFromTask(() => sync.SyncNowAsync());
        sync.Status.ObserveOn(ReactiveUI.RxSchedulers.MainThreadScheduler).Subscribe(status =>
        {
            SyncState = status.State;
            SyncMessage = status.LastSyncedAt is null
                ? status.Message
                : $"{status.Message} · {status.LastSyncedAt:HH:mm}";
        });
    }

    public LibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }
    public ReactiveCommand<Unit, Unit> ShowLibraryCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncCommand { get; }

    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentPage, value);
            this.RaisePropertyChanged(nameof(ShowSidebar));
        }
    }

    public bool ShowSidebar => CurrentPage is not ReaderViewModel;

    public string SyncMessage
    {
        get => _syncMessage;
        private set => this.RaiseAndSetIfChanged(ref _syncMessage, value);
    }

    public SyncState SyncState
    {
        get => _syncState;
        private set => this.RaiseAndSetIfChanged(ref _syncState, value);
    }

    public async Task InitializeAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        System.IO.Directory.CreateDirectory(_settings.Current.LibraryPath);
        await _categories.EnsureFavoritesAsync(default);
        _watcher.Start();
        _settings.Changes.Subscribe(_ => _watcher.Start());
        CurrentPage = Library;
        await Library.LoadAsync();
        _scheduler.Start();
    }

    public async Task ShowLibraryAsync()
    {
        if (_reader is not null)
        {
            await _reader.FlushAsync();
            _reader = null;
            await _scheduler.OnBookOpenCloseAsync();
        }

        CurrentPage = Library;
        await Library.LoadAsync();
    }

    public Task ShowSettingsAsync()
    {
        CurrentPage = Settings;
        return Task.CompletedTask;
    }

    public async Task OpenBookAsync(long bookId)
    {
        var reader = _services.GetRequiredService<ReaderViewModel>();
        var opened = await reader.LoadAsync(bookId);
        if (!opened)
            return;
        _reader = reader;
        CurrentPage = reader;
        await _scheduler.OnBookOpenCloseAsync();
    }

    public async Task ShutdownAsync()
    {
        if (_reader is not null)
            await _reader.FlushAsync();
        _watcher.Dispose();
        _scheduler.Dispose();
    }
}
