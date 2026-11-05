using System.Reactive.Linq;
using System.Threading.Tasks;
using MoonBooks.Services;
using ReactiveUI;

namespace MoonBooks.ViewModels;

public sealed partial class SettingsViewModel
{
    private SyncProviderKind _syncProvider;
    private SyncMode _syncMode;
    private string _webDavUrl = string.Empty;
    private string _webDavUser = string.Empty;
    private string _webDavPassword = string.Empty;
    private string _googleClientId = string.Empty;
    private string _googleClientSecret = string.Empty;
    private string _googleDriveFolderId = string.Empty;
    private string _booksFolder = "Books";
    private decimal _syncInterval = 10;
    private bool _autoAccept;
    private string _syncHint = "drive.file не видит файлы Moon+. Нужен scope drive.";

    public void AttachSync(ISecretStore secrets, ISyncService sync)
    {
        var current = _settings.Current;
        SyncProvider = current.SyncProvider;
        SyncMode = current.SyncMode;
        WebDavUrl = current.WebDavUrl;
        WebDavUser = current.WebDavUser;
        GoogleClientId = current.GoogleClientId;
        GoogleDriveFolderId = current.GoogleDriveFolderId;
        BooksFolder = current.MoonPlusBooksFolder;
        SyncInterval = current.SyncIntervalMinutes;
        AutoAccept = current.AutoAcceptRemotePosition;
        SaveSyncCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (!string.IsNullOrEmpty(WebDavPassword))
                await secrets.SetAsync(SecretKeys.WebDavPassword, WebDavPassword, default);
            if (!string.IsNullOrEmpty(GoogleClientSecret))
                await secrets.SetAsync(SecretKeys.GoogleClientSecret, GoogleClientSecret, default);
            await _settings.UpdateAsync(settings =>
            {
                settings.SyncProvider = SyncProvider;
                settings.SyncMode = SyncMode;
                settings.WebDavUrl = WebDavUrl;
                settings.WebDavUser = WebDavUser;
                settings.GoogleClientId = GoogleClientId;
                settings.GoogleDriveFolderId = GoogleDriveFolderId;
                settings.MoonPlusBooksFolder = string.IsNullOrWhiteSpace(BooksFolder) ? "Books" : BooksFolder;
                settings.SyncIntervalMinutes = (int)SyncInterval;
                settings.AutoAcceptRemotePosition = AutoAccept;
                settings.WatchLibrary = WatchLibrary;
            });
            WebDavPassword = string.Empty;
            GoogleClientSecret = string.Empty;
            Status = "Параметры синхронизации сохранены. Секреты записаны в локальное хранилище.";
        });
        TestSyncCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveSyncCommand.Execute().FirstAsync();
            await sync.SyncNowAsync();
            Status = "Проверка соединения запущена.";
        });
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveSyncCommand { get; private set; } =
        ReactiveCommand.Create(() => { });

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> TestSyncCommand { get; private set; } =
        ReactiveCommand.Create(() => { });

    public SyncProviderKind SyncProvider
    {
        get => _syncProvider;
        set => this.RaiseAndSetIfChanged(ref _syncProvider, value);
    }

    public SyncMode SyncMode
    {
        get => _syncMode;
        set => this.RaiseAndSetIfChanged(ref _syncMode, value);
    }

    public string WebDavUrl
    {
        get => _webDavUrl;
        set => this.RaiseAndSetIfChanged(ref _webDavUrl, value);
    }

    public string WebDavUser
    {
        get => _webDavUser;
        set => this.RaiseAndSetIfChanged(ref _webDavUser, value);
    }

    public string WebDavPassword
    {
        get => _webDavPassword;
        set => this.RaiseAndSetIfChanged(ref _webDavPassword, value);
    }

    public string GoogleClientId
    {
        get => _googleClientId;
        set => this.RaiseAndSetIfChanged(ref _googleClientId, value);
    }

    public string GoogleClientSecret
    {
        get => _googleClientSecret;
        set => this.RaiseAndSetIfChanged(ref _googleClientSecret, value);
    }

    public string GoogleDriveFolderId
    {
        get => _googleDriveFolderId;
        set => this.RaiseAndSetIfChanged(ref _googleDriveFolderId, value);
    }

    public string BooksFolder
    {
        get => _booksFolder;
        set => this.RaiseAndSetIfChanged(ref _booksFolder, value);
    }

    public decimal SyncInterval
    {
        get => _syncInterval;
        set => this.RaiseAndSetIfChanged(ref _syncInterval, value);
    }

    public bool AutoAccept
    {
        get => _autoAccept;
        set => this.RaiseAndSetIfChanged(ref _autoAccept, value);
    }

    public string SyncHint
    {
        get => _syncHint;
        private set => this.RaiseAndSetIfChanged(ref _syncHint, value);
    }
}
