using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MusicLibraryManager.ViewModels;

namespace MusicLibraryManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.HostWindow = this;
        };
        Opened += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.HostWindow = this;
        };
        Closing += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.Shutdown();
        };
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!e.Data.Contains(DataFormats.Files)) return;

        var files = e.Data.GetFiles();
        if (files == null) return;

        var paths = new List<string>();
        foreach (var item in files)
        {
            if (item is IStorageFile sf)
            {
                var path = sf.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            else if (item is IStorageFolder folder)
            {
                var path = folder.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    vm.BatchFolder = path;
            }
        }

        if (paths.Count > 0)
            vm.ApplyDroppedFiles(paths);

        await Task.CompletedTask;
    }
}
