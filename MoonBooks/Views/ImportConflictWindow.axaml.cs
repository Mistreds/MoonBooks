using Avalonia.Controls;
using Avalonia.Interactivity;
using MoonBooks.Services;

namespace MoonBooks.Views;

public partial class ImportConflictWindow : Window
{
    public ImportConflictWindow()
    {
        InitializeComponent();
    }

    public ConflictResolution? Choice { get; private set; }

    public static ImportConflictWindow ForFile(string fileName) => new()
    {
        FileNameText = { Text = fileName },
    };

    private void Replace_OnClick(object? sender, RoutedEventArgs e) => CloseWith(ConflictResolution.Replace);

    private void KeepBoth_OnClick(object? sender, RoutedEventArgs e) => CloseWith(ConflictResolution.KeepBoth);

    private void Skip_OnClick(object? sender, RoutedEventArgs e) => CloseWith(ConflictResolution.Skip);

    private void CloseWith(ConflictResolution choice)
    {
        Choice = choice;
        Close();
    }
}
