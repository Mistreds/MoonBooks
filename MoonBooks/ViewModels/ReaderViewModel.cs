using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Content;
using MoonBooks.Core.Parsing;
using MoonBooks.Core.Reading;
using MoonBooks.Reading;
using MoonBooks.Services;
using ReactiveUI;

namespace MoonBooks.ViewModels;

public sealed class ReaderViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly ISettingsService _settings;
    private readonly IEnumerable<IBookParser> _parsers;
    private readonly PageLayoutEngine _layout = new();
    private BookContent? _content;
    private string? _path;
    private long _bookId;
    private int _totalChars;
    private IReadOnlyList<PageSlice> _pages = [];
    private int _pageIndex;
    private bool _restoring;
    private double _viewportWidth = 900;
    private double _viewportHeight = 700;
    private string _title = string.Empty;
    private string _chapterTitle = string.Empty;
    private double _percent;
    private bool _animate = true;
    private bool _scrollMode;
    private string? _footnote;
    private bool _barsVisible = true;
    private PageSlice? _leftPage;
    private PageSlice? _rightPage;
    private bool _spread;

    public ReaderViewModel(ILibraryService library, ISettingsService settings, IEnumerable<IBookParser> parsers)
    {
        _library = library;
        _settings = settings;
        _parsers = parsers;
        var reader = settings.Current.Reader;
        _animate = reader.PageAnimation;
        _scrollMode = reader.ScrollMode;
        BackCommand = ReactiveCommand.Create(() => { });
        SelectTocCommand = ReactiveCommand.Create<TocEntry>(SelectToc);
        NextPageCommand = ReactiveCommand.Create(NextPage);
        PreviousPageCommand = ReactiveCommand.Create(PreviousPage);
        ToggleBookmarkCommand = ReactiveCommand.CreateFromTask(ToggleBookmarkAsync);
        CloseFootnoteCommand = ReactiveCommand.Create(() => { Footnote = null; });
        IncreaseFontCommand = ReactiveCommand.CreateFromTask(() => ChangeFontAsync(1));
        DecreaseFontCommand = ReactiveCommand.CreateFromTask(() => ChangeFontAsync(-1));
        SetThemeCommand = ReactiveCommand.CreateFromTask<ReaderThemeKind>(SetThemeAsync);
        ToggleScrollCommand = ReactiveCommand.CreateFromTask(ToggleScrollAsync);
        ToggleHyphenationCommand = ReactiveCommand.CreateFromTask(ToggleHyphenationAsync);
        ToggleBionicCommand = ReactiveCommand.CreateFromTask(ToggleBionicAsync);
        ToggleAnimationCommand = ReactiveCommand.CreateFromTask(ToggleAnimationAsync);
        CycleSpacingCommand = ReactiveCommand.CreateFromTask(CycleSpacingAsync);
        CycleAlignmentCommand = ReactiveCommand.CreateFromTask(CycleAlignmentAsync);

        this.WhenAnyValue(view => view.Percent)
            .Skip(1)
            .Throttle(TimeSpan.FromSeconds(2))
            .ObserveOn(ReactiveUI.RxSchedulers.TaskpoolScheduler)
            .Subscribe(async _ => await FlushAsync());
    }

    public string Title
    {
        get => _title;
        private set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string ChapterTitle
    {
        get => _chapterTitle;
        private set => this.RaiseAndSetIfChanged(ref _chapterTitle, value);
    }

    public double Percent
    {
        get => _percent;
        set
        {
            if (Math.Abs(_percent - value) < 0.01)
                return;
            this.RaiseAndSetIfChanged(ref _percent, value);
            if (!_restoring)
                SeekPercent(value);
        }
    }

    public bool Animate
    {
        get => _animate;
        private set => this.RaiseAndSetIfChanged(ref _animate, value);
    }

    public bool ScrollMode
    {
        get => _scrollMode;
        private set => this.RaiseAndSetIfChanged(ref _scrollMode, value);
    }

    public string? Footnote
    {
        get => _footnote;
        private set
        {
            this.RaiseAndSetIfChanged(ref _footnote, value);
            this.RaisePropertyChanged(nameof(HasFootnote));
        }
    }

    public bool HasFootnote => !string.IsNullOrWhiteSpace(Footnote);

    public bool BarsVisible
    {
        get => _barsVisible;
        set => this.RaiseAndSetIfChanged(ref _barsVisible, value);
    }

    public PageSlice? LeftPage
    {
        get => _leftPage;
        private set => this.RaiseAndSetIfChanged(ref _leftPage, value);
    }

    public PageSlice? RightPage
    {
        get => _rightPage;
        private set => this.RaiseAndSetIfChanged(ref _rightPage, value);
    }

    public bool Spread
    {
        get => _spread;
        private set => this.RaiseAndSetIfChanged(ref _spread, value);
    }

    public BookContent? Content => _content;
    public ReaderSettings Settings => _settings.Current.Reader;
    public IReadOnlyList<TocEntry> Toc => _content?.Toc ?? [];
    public int ChapterIndex { get; private set; }
    public int CharOffset { get; private set; }
    public string FontLabel => Settings.FontFamily;
    public double FontSize => Settings.FontSize;

    public ReactiveCommand<TocEntry, Unit> SelectTocCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> NextPageCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleBookmarkCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseFootnoteCommand { get; }
    public ReactiveCommand<Unit, Unit> IncreaseFontCommand { get; }
    public ReactiveCommand<Unit, Unit> DecreaseFontCommand { get; }
    public ReactiveCommand<ReaderThemeKind, Unit> SetThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleScrollCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleHyphenationCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleBionicCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleAnimationCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleSpacingCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleAlignmentCommand { get; }

    public async Task<bool> LoadAsync(long bookId)
    {
        var book = await _library.GetBookAsync(bookId, CancellationToken.None);
        if (book is null || book.IsMissing)
            return false;
        _path = Path.Combine(_settings.Current.LibraryPath, book.FileName);
        if (!File.Exists(_path))
            return false;
        var parser = _parsers.FirstOrDefault(item => item.CanParse(_path));
        if (parser is null)
            return false;
        await using var stream = File.OpenRead(_path);
        _content = await parser.ReadContentAsync(stream, CancellationToken.None);
        _bookId = bookId;
        _totalChars = Math.Max(1, _content.TotalChars);
        Title = book.Title;
        var position = book.ReadingState is null
            ? BookPosition.Start
            : new BookPosition(book.ReadingState.ChapterIndex, book.ReadingState.CharOffset);
        ChapterIndex = Math.Clamp(position.ChapterIndex, 0, Math.Max(0, _content.Chapters.Count - 1));
        CharOffset = position.CharOffset;
        Relayout(keepPosition: true);
        return true;
    }

    public void SetViewport(double width, double height)
    {
        if (Math.Abs(width - _viewportWidth) < 4 && Math.Abs(height - _viewportHeight) < 4)
            return;
        _viewportWidth = width;
        _viewportHeight = height;
        Relayout(keepPosition: true);
    }

    public void ShowFootnote(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || _content is null || !_content.Footnotes.TryGet(id, out var note))
        {
            Footnote = null;
            return;
        }

        Footnote = string.IsNullOrWhiteSpace(note.Title)
            ? PlainText.FromBlocks(note.Blocks)
            : note.Title + "\n" + PlainText.FromBlocks(note.Blocks);
    }

    public void SelectToc(TocEntry entry)
    {
        ChapterIndex = entry.ChapterIndex;
        CharOffset = entry.CharOffset;
        Relayout(keepPosition: true);
    }

    public async Task FlushAsync()
    {
        if (_bookId == 0)
            return;
        await _library.SavePositionAsync(_bookId, ChapterIndex, CharOffset, Percent, CancellationToken.None);
    }

    private void NextPage()
    {
        var step = Spread ? 2 : 1;
        if (_pageIndex + step < _pages.Count)
        {
            _pageIndex += step;
            ApplyPage();
            return;
        }

        if (_content is null || ChapterIndex >= _content.Chapters.Count - 1)
            return;
        ChapterIndex++;
        CharOffset = 0;
        Relayout(keepPosition: true);
    }

    private void PreviousPage()
    {
        var step = Spread ? 2 : 1;
        if (_pageIndex - step >= 0)
        {
            _pageIndex -= step;
            ApplyPage();
            return;
        }

        if (ChapterIndex <= 0)
            return;
        ChapterIndex--;
        CharOffset = int.MaxValue;
        Relayout(keepPosition: true);
    }

    private void SeekPercent(double percent)
    {
        if (_content is null || _content.Chapters.Count == 0)
            return;
        var target = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100d * _content.TotalChars);
        var cursor = 0;
        for (var i = 0; i < _content.Chapters.Count; i++)
        {
            var length = _content.Chapters[i].PlainTextLength;
            if (cursor + length >= target || i == _content.Chapters.Count - 1)
            {
                ChapterIndex = i;
                CharOffset = Math.Clamp(target - cursor, 0, length);
                Relayout(keepPosition: true);
                return;
            }

            cursor += length;
        }
    }

    private void Relayout(bool keepPosition)
    {
        if (_content is null || _content.Chapters.Count == 0)
            return;
        ChapterIndex = Math.Clamp(ChapterIndex, 0, _content.Chapters.Count - 1);
        var chapter = _content.Chapters[ChapterIndex];
        Spread = _viewportWidth > 1100 && !ScrollMode;
        var horizontalPadding = 64;
        var gap = 48d;
        var columnWidth = Spread
            ? Math.Max(240, (_viewportWidth - horizontalPadding - gap) / 2)
            : Math.Max(280, Math.Min(760, _viewportWidth - horizontalPadding));
        var pageHeight = Math.Max(240, _viewportHeight - 72);
        var request = new PageLayoutRequest
        {
            Settings = Settings,
            ColumnWidth = columnWidth,
            PageHeight = pageHeight,
            Images = _content.Resources.ToDictionary(pair => pair.Key, pair => pair.Value.Data, StringComparer.OrdinalIgnoreCase),
        };
        _pages = _layout.LayoutChapter(ChapterIndex, chapter, request);
        if (CharOffset == int.MaxValue)
            _pageIndex = Math.Max(0, _pages.Count - (Spread ? 2 : 1));
        else if (keepPosition)
            _pageIndex = PageLayoutEngine.PageIndexFor(_pages, CharOffset);
        if (Spread && _pageIndex % 2 == 1)
            _pageIndex--;
        _pageIndex = Math.Clamp(_pageIndex, 0, Math.Max(0, _pages.Count - 1));
        ApplyPage();
        ChapterTitle = chapter.Title;
        this.RaisePropertyChanged(nameof(Toc));
        this.RaisePropertyChanged(nameof(Settings));
        this.RaisePropertyChanged(nameof(FontSize));
        this.RaisePropertyChanged(nameof(FontLabel));
    }

    private void ApplyPage()
    {
        LeftPage = _pages.Count == 0 ? null : _pages[Math.Clamp(_pageIndex, 0, _pages.Count - 1)];
        RightPage = Spread && _pageIndex + 1 < _pages.Count ? _pages[_pageIndex + 1] : null;
        if (LeftPage is not null)
        {
            ChapterIndex = LeftPage.ChapterIndex;
            CharOffset = LeftPage.StartCharOffset;
        }

        var before = 0;
        if (_content is not null)
        {
            for (var i = 0; i < ChapterIndex && i < _content.Chapters.Count; i++)
                before += _content.Chapters[i].PlainTextLength;
        }

        _restoring = true;
        Percent = Math.Clamp((before + CharOffset) * 100d / _totalChars, 0, 100);
        _restoring = false;
    }

    private async Task ChangeFontAsync(double delta)
    {
        await _settings.UpdateAsync(settings => settings.Reader.FontSize = Math.Clamp(settings.Reader.FontSize + delta, 12, 32));
        Relayout(keepPosition: true);
    }

    private async Task SetThemeAsync(ReaderThemeKind theme)
    {
        await _settings.UpdateAsync(settings => settings.Reader.Theme = theme);
        this.RaisePropertyChanged(nameof(Settings));
    }

    private async Task ToggleScrollAsync()
    {
        await _settings.UpdateAsync(settings => settings.Reader.ScrollMode = !settings.Reader.ScrollMode);
        ScrollMode = Settings.ScrollMode;
        Relayout(keepPosition: true);
    }

    private async Task ToggleHyphenationAsync()
    {
        await _settings.UpdateAsync(settings => settings.Reader.Hyphenation = !settings.Reader.Hyphenation);
        Relayout(keepPosition: true);
    }

    private async Task ToggleBionicAsync()
    {
        await _settings.UpdateAsync(settings => settings.Reader.Bionic = !settings.Reader.Bionic);
        Relayout(keepPosition: true);
    }

    private async Task ToggleAnimationAsync()
    {
        await _settings.UpdateAsync(settings => settings.Reader.PageAnimation = !settings.Reader.PageAnimation);
        Animate = Settings.PageAnimation;
    }

    private async Task CycleSpacingAsync()
    {
        await _settings.UpdateAsync(settings =>
        {
            settings.Reader.LineSpacing = settings.Reader.LineSpacing switch
            {
                LineSpacingPreset.Tight => LineSpacingPreset.Normal,
                LineSpacingPreset.Normal => LineSpacingPreset.Relaxed,
                _ => LineSpacingPreset.Tight,
            };
        });
        Relayout(keepPosition: true);
    }

    private async Task CycleAlignmentAsync()
    {
        await _settings.UpdateAsync(settings =>
            settings.Reader.Alignment = settings.Reader.Alignment == ReaderAlignment.Justify
                ? ReaderAlignment.Left
                : ReaderAlignment.Justify);
        Relayout(keepPosition: true);
    }

    private async Task ToggleBookmarkAsync()
    {
        await _settings.UpdateAsync(settings =>
        {
            var existing = settings.Bookmarks.FirstOrDefault(mark => mark.BookId == _bookId && mark.ChapterIndex == ChapterIndex && Math.Abs(mark.CharOffset - CharOffset) < 40);
            if (existing is not null)
                settings.Bookmarks.Remove(existing);
            else
            {
                settings.Bookmarks.Add(new LocalBookmark
                {
                    BookId = _bookId,
                    ChapterIndex = ChapterIndex,
                    CharOffset = CharOffset,
                    CreatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
            }
        });
    }
}
