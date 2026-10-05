using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibraryManager.ViewModels;

namespace MusicLibraryManager.Views;

public partial class CdRipperWindow : Window
{
    public CdRipperWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireHost();
        Opened += (_, _) => WireHost();
    }

    public CdRipperWindow(CdRipperViewModel vm) : this()
    {
        DataContext = vm;
        WireHost();
    }

    private void WireHost()
    {
        if (DataContext is CdRipperViewModel vm)
            vm.HostWindow = this;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
