using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using MoonBooks.Models;
using MoonBooks.Services;
using ReactiveUI;

namespace MoonBooks.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ILibraryService _libraryService;
    private readonly ISyncService _syncService;
    private readonly IReadOnlyList<BookItem> _allBooks;
    private string _searchText = string.Empty;
    private string _selectedShelf = "Все книги";
    private BookItem? _selectedBook;
    private int _chapterIndex;
    private bool _isReaderOpen;
    private bool _isTocOpen = true;
    private double _readerFontSize = 20;
    private double _readerLineHeight = 33;
    private double _readerWidth = 720;
    private string _readerTheme = "Сепия";
    private SyncState _syncState;
    private int _syncAttempt;

    public MainViewModel()
        : this(new MockLibraryService(), new MockSyncService())
    {
    }

    public MainViewModel(ILibraryService libraryService, ISyncService syncService)
    {
        _libraryService = libraryService;
        _syncService = syncService;
        _allBooks = libraryService.GetBooks();
        _syncState = syncService.State;

        Books = new ObservableCollection<BookItem>(_allBooks);
        Shelves = new ObservableCollection<ShelfItem>(libraryService.GetShelves());
        ContinueReading = new ObservableCollection<BookItem>(
            _allBooks.Where(book => book.Progress > 0 && book.Progress < 1).Take(3));

        OpenBookCommand = ReactiveCommand.Create<BookItem>(OpenBook);
        SelectShelfCommand = ReactiveCommand.Create<ShelfItem>(SelectShelf);
        GoToLibraryCommand = ReactiveCommand.Create(GoToLibrary);
        ToggleTocCommand = ReactiveCommand.Create(() => { IsTocOpen = !IsTocOpen; });
        NextChapterCommand = ReactiveCommand.Create(NextChapter);
        PreviousChapterCommand = ReactiveCommand.Create(PreviousChapter);
        SelectChapterCommand = ReactiveCommand.Create<BookChapter>(SelectChapter);
        IncreaseFontCommand = ReactiveCommand.Create(() => { ReaderFontSize = Math.Min(30, ReaderFontSize + 1); });
        DecreaseFontCommand = ReactiveCommand.Create(() => { ReaderFontSize = Math.Max(14, ReaderFontSize - 1); });
        IncreaseLineHeightCommand = ReactiveCommand.Create(() => { ReaderLineHeight = Math.Min(46, ReaderLineHeight + 2); });
        DecreaseLineHeightCommand = ReactiveCommand.Create(() => { ReaderLineHeight = Math.Max(24, ReaderLineHeight - 2); });
        ToggleReaderWidthCommand = ReactiveCommand.Create(() => { ReaderWidth = ReaderWidth < 800 ? 880 : 680; });
        SetReaderThemeCommand = ReactiveCommand.Create<string>(theme => ReaderTheme = theme);
        SyncCommand = ReactiveCommand.CreateFromTask(RunMockSyncAsync);
    }

    public ObservableCollection<BookItem> Books { get; }
    public ObservableCollection<BookItem> ContinueReading { get; }
    public ObservableCollection<ShelfItem> Shelves { get; }
    public bool HasBooks => Books.Count > 0;
    public bool IsLibraryEmpty => !HasBooks;

    public ReactiveCommand<BookItem, Unit> OpenBookCommand { get; }
    public ReactiveCommand<ShelfItem, Unit> SelectShelfCommand { get; }
    public ReactiveCommand<Unit, Unit> GoToLibraryCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleTocCommand { get; }
    public ReactiveCommand<Unit, Unit> NextChapterCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousChapterCommand { get; }
    public ReactiveCommand<BookChapter, Unit> SelectChapterCommand { get; }
    public ReactiveCommand<Unit, Unit> IncreaseFontCommand { get; }
    public ReactiveCommand<Unit, Unit> DecreaseFontCommand { get; }
    public ReactiveCommand<Unit, Unit> IncreaseLineHeightCommand { get; }
    public ReactiveCommand<Unit, Unit> DecreaseLineHeightCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleReaderWidthCommand { get; }
    public ReactiveCommand<string, Unit> SetReaderThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            ApplyFilters();
        }
    }

    public string SelectedShelf
    {
        get => _selectedShelf;
        private set => this.RaiseAndSetIfChanged(ref _selectedShelf, value);
    }

    public BookItem? SelectedBook
    {
        get => _selectedBook;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedBook, value);
            this.RaisePropertyChanged(nameof(Chapters));
            this.RaisePropertyChanged(nameof(CurrentChapter));
            this.RaisePropertyChanged(nameof(ChapterPosition));
        }
    }

    public bool IsReaderOpen
    {
        get => _isReaderOpen;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isReaderOpen, value);
            this.RaisePropertyChanged(nameof(IsLibraryOpen));
        }
    }

    public bool IsLibraryOpen => !IsReaderOpen;

    public bool IsTocOpen
    {
        get => _isTocOpen;
        set => this.RaiseAndSetIfChanged(ref _isTocOpen, value);
    }

    public IReadOnlyList<BookChapter> Chapters => SelectedBook?.Chapters ?? [];
    public BookChapter? CurrentChapter => Chapters.Count > 0 ? Chapters[_chapterIndex] : null;
    public string ChapterPosition => Chapters.Count == 0 ? string.Empty : $"{_chapterIndex + 1} из {Chapters.Count}";

    public double ReaderFontSize
    {
        get => _readerFontSize;
        set => this.RaiseAndSetIfChanged(ref _readerFontSize, value);
    }

    public double ReaderLineHeight
    {
        get => _readerLineHeight;
        set => this.RaiseAndSetIfChanged(ref _readerLineHeight, value);
    }

    public double ReaderWidth
    {
        get => _readerWidth;
        set => this.RaiseAndSetIfChanged(ref _readerWidth, value);
    }

    public string ReaderTheme
    {
        get => _readerTheme;
        set
        {
            this.RaiseAndSetIfChanged(ref _readerTheme, value);
            this.RaisePropertyChanged(nameof(ReaderPageBackground));
            this.RaisePropertyChanged(nameof(ReaderTextColor));
            this.RaisePropertyChanged(nameof(ReaderMutedColor));
            this.RaisePropertyChanged(nameof(ReaderCanvasBackground));
        }
    }

    public string ReaderPageBackground => ReaderTheme switch
    {
        "Ночь" => "#1D2228",
        "Светлая" => "#FFFDFC",
        _ => "#F7F0E3",
    };

    public string ReaderCanvasBackground => ReaderTheme switch
    {
        "Ночь" => "#101419",
        "Светлая" => "#F2F0EC",
        _ => "#DCD3C3",
    };

    public string ReaderTextColor => ReaderTheme == "Ночь" ? "#D8D7D2" : "#2B2925";
    public string ReaderMutedColor => ReaderTheme == "Ночь" ? "#898D91" : "#81786D";

    public SyncState SyncState
    {
        get => _syncState;
        private set
        {
            this.RaiseAndSetIfChanged(ref _syncState, value);
            this.RaisePropertyChanged(nameof(SyncTitle));
            this.RaisePropertyChanged(nameof(SyncSubtitle));
            this.RaisePropertyChanged(nameof(SyncGlyph));
            this.RaisePropertyChanged(nameof(IsSyncing));
        }
    }

    public bool IsSyncing => SyncState == MoonBooks.Models.SyncState.Syncing;
    public string SyncTitle => SyncState switch
    {
        MoonBooks.Models.SyncState.Syncing => "Синхронизируем…",
        MoonBooks.Models.SyncState.Error => "Нужна проверка",
        _ => "Google Drive подключён",
    };
    public string SyncSubtitle => SyncState switch
    {
        MoonBooks.Models.SyncState.Syncing => "Обновляем полки и позиции чтения",
        MoonBooks.Models.SyncState.Error => "Не удалось получить свежие данные",
        _ => "Последняя синхронизация · 2 минуты назад",
    };
    public string SyncGlyph => SyncState switch
    {
        MoonBooks.Models.SyncState.Syncing => "↻",
        MoonBooks.Models.SyncState.Error => "!",
        _ => "✓",
    };

    private void ApplyFilters()
    {
        var filtered = _allBooks.Where(book =>
            (SelectedShelf == "Все книги" || book.Shelf == SelectedShelf) &&
            (string.IsNullOrWhiteSpace(SearchText) ||
             book.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             book.Author.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             book.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

        Books.Clear();
        foreach (var book in filtered)
            Books.Add(book);

        this.RaisePropertyChanged(nameof(HasBooks));
        this.RaisePropertyChanged(nameof(IsLibraryEmpty));
    }

    private void SelectShelf(ShelfItem shelf)
    {
        SelectedShelf = shelf.Name;
        ApplyFilters();
    }

    private void OpenBook(BookItem book)
    {
        _chapterIndex = Math.Clamp((int)(book.Progress * book.Chapters.Count), 0, book.Chapters.Count - 1);
        SelectedBook = book;
        IsReaderOpen = true;
    }

    private void GoToLibrary() => IsReaderOpen = false;

    private void NextChapter()
    {
        if (_chapterIndex >= Chapters.Count - 1)
            return;

        _chapterIndex++;
        RaiseChapterChanged();
    }

    private void PreviousChapter()
    {
        if (_chapterIndex <= 0)
            return;

        _chapterIndex--;
        RaiseChapterChanged();
    }

    private void SelectChapter(BookChapter chapter)
    {
        var index = -1;
        for (var i = 0; i < Chapters.Count; i++)
        {
            if (Chapters[i] == chapter)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return;

        _chapterIndex = index;
        RaiseChapterChanged();
    }

    private void RaiseChapterChanged()
    {
        this.RaisePropertyChanged(nameof(CurrentChapter));
        this.RaisePropertyChanged(nameof(ChapterPosition));
    }

    private async Task RunMockSyncAsync()
    {
        _syncService.BeginSync();
        SyncState = _syncService.State;
        await Task.Delay(900);
        _syncAttempt++;
        if (_syncAttempt % 3 == 0)
            _syncService.FailSync();
        else
            _syncService.CompleteSync();
        SyncState = _syncService.State;
    }
}