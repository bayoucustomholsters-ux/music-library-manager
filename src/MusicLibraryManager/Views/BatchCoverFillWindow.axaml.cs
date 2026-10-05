using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibraryManager.ViewModels;

namespace MusicLibraryManager.Views;

public partial class BatchCoverFillWindow : Window
{
    public BatchCoverFillWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireHost();
        Opened += (_, _) => WireHost();
    }

    public BatchCoverFillWindow(BatchCoverFillViewModel vm) : this()
    {
        DataContext = vm;
        WireHost();
    }

    private void WireHost()
    {
        if (DataContext is BatchCoverFillViewModel vm)
            vm.HostWindow = this;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
