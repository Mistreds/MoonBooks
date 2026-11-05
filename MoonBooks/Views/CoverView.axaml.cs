using Avalonia.Controls;
using MoonBooks.ViewModels;

namespace MoonBooks.Views;

public partial class CoverView : UserControl
{
    public CoverView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is BookCardViewModel card)
                card.EnsureCover();
        };
    }
}
