using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibraryManager.ViewModels;

namespace MusicLibraryManager.Views;

public partial class DuplicateFinderWindow : Window
{
    public DuplicateFinderWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireHost();
        Opened += (_, _) => WireHost();
    }

    public DuplicateFinderWindow(DuplicateFinderViewModel vm) : this()
    {
        DataContext = vm;
        WireHost();
    }

    private void WireHost()
    {
        if (DataContext is DuplicateFinderViewModel vm)
            vm.HostWindow = this;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
