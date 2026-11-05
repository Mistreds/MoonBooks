using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoonBooks.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, string confirm, string? cancel)
        : this()
    {
        Title = title;
        HeadingText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirm;
        if (string.IsNullOrWhiteSpace(cancel))
            CancelButton.IsVisible = false;
        else
            CancelButton.Content = cancel;
    }

    public bool Confirmed { get; private set; }

    private void Confirm_OnClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();
}
