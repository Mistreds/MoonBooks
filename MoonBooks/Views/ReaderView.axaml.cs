using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MoonBooks.Services;
using MoonBooks.ViewModels;

namespace MoonBooks.Views;

public partial class ReaderView : UserControl
{
    private readonly DispatcherTimer _hideTimer;
    private ResourceDictionary? _theme;

    public ReaderView()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (DataContext is ReaderViewModel viewModel)
                viewModel.BarsVisible = false;
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ReaderViewModel viewModel)
                return;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTheme(viewModel);
            ShowBars();
            Focus();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ReaderViewModel viewModel && e.PropertyName is nameof(ReaderViewModel.Settings) or nameof(ReaderViewModel.Animate))
            ApplyTheme(viewModel);
    }

    private void ShowBars()
    {
        if (DataContext is ReaderViewModel viewModel)
            viewModel.BarsVisible = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void ApplyTheme(ReaderViewModel viewModel)
    {
        var key = viewModel.Settings.Theme switch
        {
            ReaderThemeKind.White => "ReaderThemeWhite",
            ReaderThemeKind.Gray => "ReaderThemeGray",
            ReaderThemeKind.Black => "ReaderThemeBlack",
            _ => "ReaderThemeSepia",
        };
        if (Application.Current?.Resources.TryGetResource(key, null, out var value) == true && value is ResourceDictionary dictionary)
        {
            if (_theme is not null)
                Resources.MergedDictionaries.Remove(_theme);
            _theme = dictionary;
            Resources.MergedDictionaries.Add(dictionary);
        }

        PageHost.Transitions = viewModel.Animate
            ? new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) } }
            : null;
    }

    private void Host_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ReaderViewModel viewModel)
            viewModel.SetViewport(e.NewSize.Width, e.NewSize.Height);
    }

    private void Surface_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (point.Y < 72 || point.Y > Bounds.Height - 72)
            ShowBars();
    }

    private void Surface_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel viewModel)
            return;
        var point = e.GetPosition(Surface);
        var third = Math.Max(1, Surface.Bounds.Width / 3);
        if (point.X < third)
            viewModel.PreviousPageCommand.Execute().Subscribe();
        else if (point.X > third * 2)
            viewModel.NextPageCommand.Execute().Subscribe();
        else
            ShowBars();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not ReaderViewModel viewModel || viewModel.ScrollMode)
            return;
        if (e.Key is Key.Right or Key.PageDown or Key.Space)
            viewModel.NextPageCommand.Execute().Subscribe();
        else if (e.Key is Key.Left or Key.PageUp)
            viewModel.PreviousPageCommand.Execute().Subscribe();
    }

    private void Surface_OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ReaderViewModel viewModel || viewModel.ScrollMode)
            return;
        if (e.Delta.Y < 0)
            viewModel.NextPageCommand.Execute().Subscribe();
        else if (e.Delta.Y > 0)
            viewModel.PreviousPageCommand.Execute().Subscribe();
        e.Handled = true;
    }

    private async void Back_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: ShellViewModel shell })
            await shell.ShowLibraryAsync();
    }

    private void Page_OnFootnote(object? sender, string id)
    {
        if (DataContext is ReaderViewModel viewModel)
            viewModel.ShowFootnote(id);
    }
}
