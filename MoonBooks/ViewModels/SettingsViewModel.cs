using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using MoonBooks.Services;
using ReactiveUI;

namespace MoonBooks.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILibraryFolderService _libraryFolder;
    private string _libraryPath = string.Empty;
    private bool _moveBooks = true;
    private LibraryViewMode _viewMode;
    private double _fontSize = 18;
    private LineSpacingPreset _lineSpacing;
    private ReaderThemeKind _theme;
    private ReaderAlignment _alignment;
    private bool _hyphenation;
    private bool _pageAnimation = true;
    private bool _scrollMode;
    private string _fontFamily = "PT Serif";
    private bool _bionic;
    private bool _watchLibrary = true;
    private string _status = string.Empty;

    public SettingsViewModel(ISettingsService settings, ILibraryFolderService libraryFolder)
    {
        _settings = settings;
        _libraryFolder = libraryFolder;
        Read(settings.Current);

        SaveReaderCommand = ReactiveCommand.CreateFromTask(SaveReaderAsync);
        ChangeLibraryCommand = ReactiveCommand.CreateFromTask(ChangeLibraryAsync);
        SetViewModeCommand = ReactiveCommand.CreateFromTask<LibraryViewMode>(SetViewModeAsync);
    }

    public IReadOnlyList<string> FontChoices { get; } =
        ["PT Serif", "Times New Roman", "Arial", "Verdana"];

    public ReactiveCommand<Unit, Unit> SaveReaderCommand { get; }
    public ReactiveCommand<Unit, Unit> ChangeLibraryCommand { get; }
    public ReactiveCommand<LibraryViewMode, Unit> SetViewModeCommand { get; }

    public string LibraryPath
    {
        get => _libraryPath;
        set => this.RaiseAndSetIfChanged(ref _libraryPath, value);
    }

    public bool MoveBooks
    {
        get => _moveBooks;
        set => this.RaiseAndSetIfChanged(ref _moveBooks, value);
    }

    public LibraryViewMode ViewMode
    {
        get => _viewMode;
        private set => this.RaiseAndSetIfChanged(ref _viewMode, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => this.RaiseAndSetIfChanged(ref _fontSize, value);
    }

    public LineSpacingPreset LineSpacing
    {
        get => _lineSpacing;
        set => this.RaiseAndSetIfChanged(ref _lineSpacing, value);
    }

    public ReaderThemeKind Theme
    {
        get => _theme;
        set => this.RaiseAndSetIfChanged(ref _theme, value);
    }

    public ReaderAlignment Alignment
    {
        get => _alignment;
        set => this.RaiseAndSetIfChanged(ref _alignment, value);
    }

    public bool Hyphenation
    {
        get => _hyphenation;
        set => this.RaiseAndSetIfChanged(ref _hyphenation, value);
    }

    public bool PageAnimation
    {
        get => _pageAnimation;
        set => this.RaiseAndSetIfChanged(ref _pageAnimation, value);
    }

    public bool ScrollMode
    {
        get => _scrollMode;
        set => this.RaiseAndSetIfChanged(ref _scrollMode, value);
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => this.RaiseAndSetIfChanged(ref _fontFamily, value);
    }

    public bool Bionic
    {
        get => _bionic;
        set => this.RaiseAndSetIfChanged(ref _bionic, value);
    }

    public bool WatchLibrary
    {
        get => _watchLibrary;
        set => this.RaiseAndSetIfChanged(ref _watchLibrary, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public async Task ChangeLibraryAsync()
    {
        var result = await _libraryFolder.ChangeFolderAsync(LibraryPath, MoveBooks, default);
        Status = result.Success
            ? result.Moved > 0 ? $"Папка изменена, перенесено книг: {result.Moved}." : "Папка библиотеки сохранена."
            : result.Error ?? "Не удалось сменить папку.";
        LibraryPath = _settings.Current.LibraryPath;
    }

    private async Task SetViewModeAsync(LibraryViewMode mode)
    {
        ViewMode = mode;
        await _settings.UpdateAsync(settings => settings.ViewMode = mode);
    }

    private async Task SaveReaderAsync()
    {
        FontSize = Math.Clamp(FontSize, 12, 32);
        await _settings.UpdateAsync(settings =>
        {
            settings.Reader.FontSize = FontSize;
            settings.Reader.LineSpacing = LineSpacing;
            settings.Reader.Theme = Theme;
            settings.Reader.Alignment = Alignment;
            settings.Reader.Hyphenation = Hyphenation;
            settings.Reader.PageAnimation = PageAnimation;
            settings.Reader.ScrollMode = ScrollMode;
            settings.Reader.FontFamily = FontFamily;
            settings.Reader.Bionic = Bionic;
            settings.WatchLibrary = WatchLibrary;
            settings.ViewMode = ViewMode;
        });
        Status = "Настройки чтения сохранены.";
    }

    private void Read(AppSettings settings)
    {
        _libraryPath = settings.LibraryPath;
        _viewMode = settings.ViewMode;
        _fontSize = settings.Reader.FontSize;
        _lineSpacing = settings.Reader.LineSpacing;
        _theme = settings.Reader.Theme;
        _alignment = settings.Reader.Alignment;
        _hyphenation = settings.Reader.Hyphenation;
        _pageAnimation = settings.Reader.PageAnimation;
        _scrollMode = settings.Reader.ScrollMode;
        _fontFamily = settings.Reader.FontFamily;
        _bionic = settings.Reader.Bionic;
        _watchLibrary = settings.WatchLibrary;
    }
}
