using System.Collections.ObjectModel;
using System.Reactive.Linq;
using MoonBooks.Models;
using ReactiveUI.Fody.Helpers;

namespace MoonBooks.ViewModels;

public class LibraryViewModel : ViewModelBase
{
    [Reactive]
    public ObservableCollection<BookModel> Books { get; set; }
    
    
}