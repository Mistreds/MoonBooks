using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MoonBooks.ViewModels;

namespace MoonBooks.Views;

public partial class LibraryView : UserControl
{
    private readonly DispatcherTimer _sizeTimer;
    private double _pendingWidth;

    public LibraryView()
    {
        InitializeComponent();
        _sizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _sizeTimer.Tick += (_, _) =>
        {
            _sizeTimer.Stop();
            if (DataContext is LibraryViewModel viewModel)
                viewModel.SetBooksPerRow(_pendingWidth);
        };
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void Host_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _pendingWidth = e.NewSize.Width;
        _sizeTimer.Stop();
        _sizeTimer.Start();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
            viewModel.IsDropTarget = e.DataTransfer.Contains(DataFormat.File);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
            viewModel.IsDropTarget = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
            return;
        viewModel.IsDropTarget = false;
        var paths = new System.Collections.Generic.List<string>();
        foreach (var item in e.DataTransfer.TryGetFiles() ?? [])
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        await viewModel.ImportPathsAsync(paths);
    }
}
