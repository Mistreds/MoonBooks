using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MoonBooks.Core.Content;
using MoonBooks.Reading;
using MoonBooks.Services;

namespace MoonBooks.Views;

public class PagePresenter : UserControl
{
    public static readonly StyledProperty<PageSlice?> SliceProperty =
        AvaloniaProperty.Register<PagePresenter, PageSlice?>(nameof(Slice));

    public static readonly StyledProperty<ReaderSettings?> ReaderSettingsProperty =
        AvaloniaProperty.Register<PagePresenter, ReaderSettings?>(nameof(ReaderSettings));

    public static readonly StyledProperty<IReadOnlyDictionary<string, byte[]>?> ImagesProperty =
        AvaloniaProperty.Register<PagePresenter, IReadOnlyDictionary<string, byte[]>?>(nameof(Images));

    private readonly StackPanel _panel = new() { Spacing = 10 };

    public PagePresenter()
    {
        Content = _panel;
    }

    public event EventHandler<string>? FootnoteInvoked;

    public PageSlice? Slice
    {
        get => GetValue(SliceProperty);
        set => SetValue(SliceProperty, value);
    }

    public ReaderSettings? ReaderSettings
    {
        get => GetValue(ReaderSettingsProperty);
        set => SetValue(ReaderSettingsProperty, value);
    }

    public IReadOnlyDictionary<string, byte[]>? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SliceProperty || change.Property == ReaderSettingsProperty || change.Property == ImagesProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        var slice = Slice;
        var settings = ReaderSettings ?? new ReaderSettings();
        if (slice is null)
            return;
        _panel.Spacing = settings.FontSize * 0.55;
        foreach (var fragment in slice.Fragments)
            _panel.Children.Add(Build(fragment, settings));
    }

    private Control Build(PageFragment fragment, ReaderSettings settings)
    {
        var font = Typography.ResolveFont(settings.FontFamily);
        var align = settings.Alignment == ReaderAlignment.Justify ? TextAlignment.Justify : TextAlignment.Left;
        if (fragment.Source is ImageBlock image)
            return BuildImage(image);

        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = font,
            FontSize = fragment.Source is HeadingBlock ? settings.FontSize * 1.35 : settings.FontSize,
            LineHeight = settings.FontSize * settings.LineHeightFactor,
            TextAlignment = fragment.Source is HeadingBlock or PoemBlock ? TextAlignment.Left : align,
            FontStyle = fragment.Source is EpigraphBlock ? FontStyle.Italic : FontStyle.Normal,
            Margin = fragment.Source is EpigraphBlock ? new Thickness(24, 0, 0, 0) : new Thickness(0),
        };

        if (settings.Bionic && fragment.Source is not HeadingBlock)
        {
            foreach (var (text, bold) in Typography.BionicPieces(fragment.Text))
            {
                block.Inlines!.Add(new Run(text)
                {
                    FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                });
            }
        }
        else if (!fragment.Partial && fragment.Source is ParagraphBlock paragraph)
        {
            foreach (var run in paragraph.Inlines)
            {
                var inline = new Run(run.Text)
                {
                    FontStyle = run.Style.HasFlag(InlineStyle.Emphasis) ? FontStyle.Italic : FontStyle.Normal,
                    FontWeight = run.Style.HasFlag(InlineStyle.Strong) ? FontWeight.SemiBold : FontWeight.Normal,
                };
                block.Inlines!.Add(inline);
                if (!string.IsNullOrWhiteSpace(run.FootnoteId))
                {
                    var id = run.FootnoteId;
                    var link = new Button
                    {
                        Content = run.Text,
                        Padding = new Thickness(0),
                        Background = Brushes.Transparent,
                        FontSize = settings.FontSize * 0.7,
                        VerticalAlignment = VerticalAlignment.Top,
                    };
                    link.Click += (_, _) => FootnoteInvoked?.Invoke(this, id);
                    block.Inlines.Add(new InlineUIContainer { Child = link });
                }
            }

            if (paragraph.Inlines.Count == 0)
                block.Text = fragment.Text;
        }
        else
        {
            block.Text = fragment.Text;
        }

        return block;
    }

    private Control BuildImage(ImageBlock image)
    {
        if (Images is null || !Images.TryGetValue(image.ResourceId, out var bytes))
            return new Border { Height = 24 };
        try
        {
            var bitmap = new Bitmap(new MemoryStream(bytes));
            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxHeight = 520,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }
        catch (Exception)
        {
            return new Border { Height = 24 };
        }
    }
}
