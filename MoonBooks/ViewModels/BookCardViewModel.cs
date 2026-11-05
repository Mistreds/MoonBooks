using System;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MoonBooks.Core.Domain;
using ReactiveUI;

namespace MoonBooks.ViewModels;

public sealed class BookCardViewModel : ViewModelBase
{
    private static readonly Color[] Palette =
    [
        Color.Parse("#3E4C45"),
        Color.Parse("#4C453E"),
        Color.Parse("#3E4654"),
        Color.Parse("#4A443C"),
        Color.Parse("#453E4C"),
        Color.Parse("#3A4946"),
    ];

    private Bitmap? _cover;
    private bool _coverRequested;

    public BookCardViewModel(Book book, string? coverPath, Func<BookCardViewModel, Task> open, Func<BookCardViewModel, Task> categories, Func<BookCardViewModel, Task> favorite, Func<BookCardViewModel, Task> delete)
    {
        Id = book.Id;
        Title = book.Title;
        Author = string.Join(", ", book.Authors.OrderBy(link => link.Order).Select(link => link.Author.Name));
        if (string.IsNullOrWhiteSpace(Author))
            Author = "Автор не указан";
        Format = book.Format == BookFormat.Fb2 ? "FB2" : "EPUB";
        Progress = book.ReadingState?.Percent ?? 0;
        ProgressLabel = book.ReadingState is null || book.ReadingState.Status == BookStatus.NotReading
            ? "Не начата"
            : book.ReadingState.Status == BookStatus.Finished
                ? "Прочитана"
                : $"{Progress:0}%";
        CoverPath = coverPath;
        PlaceholderBrush = new SolidColorBrush(Palette[Math.Abs(StableHash(Title)) % Palette.Length]);
        IsMissing = book.IsMissing;
        OpenCommand = ReactiveCommand.CreateFromTask(() => open(this));
        CategoriesCommand = ReactiveCommand.CreateFromTask(() => categories(this));
        FavoriteCommand = ReactiveCommand.CreateFromTask(() => favorite(this));
        DeleteCommand = ReactiveCommand.CreateFromTask(() => delete(this));
    }

    public long Id { get; }
    public string Title { get; }
    public string Author { get; }
    public string Format { get; }
    public double Progress { get; }
    public string ProgressLabel { get; }
    public string? CoverPath { get; }
    public IBrush PlaceholderBrush { get; }
    public bool IsMissing { get; }
    public bool HasCover => _cover is not null;

    public Bitmap? Cover
    {
        get => _cover;
        private set
        {
            this.RaiseAndSetIfChanged(ref _cover, value);
            this.RaisePropertyChanged(nameof(HasCover));
        }
    }

    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> CategoriesCommand { get; }
    public ReactiveCommand<Unit, Unit> FavoriteCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    public void EnsureCover()
    {
        if (_coverRequested || string.IsNullOrWhiteSpace(CoverPath))
            return;
        _coverRequested = true;
        try
        {
            Cover = new Bitmap(CoverPath);
        }
        catch (Exception)
        {
            Cover = null;
        }
    }

    private static int StableHash(string value)
    {
        var hash = 17;
        foreach (var ch in value)
            hash = hash * 31 + ch;
        return hash;
    }
}
