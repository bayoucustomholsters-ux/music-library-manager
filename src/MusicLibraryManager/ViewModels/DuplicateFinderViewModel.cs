using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.ViewModels;

public partial class DuplicateFinderViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly Action<string> _appendMainLog;
    private CancellationTokenSource? _cts;
    private string _scanRoot = string.Empty;

    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _statusText = "Pick a folder and scan.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private DuplicateGroup? _selectedGroup;
    [ObservableProperty] private TrackScanInfo? _selectedTrack;
    [ObservableProperty] private bool _confirmQuarantine;

    public ObservableCollection<DuplicateGroup> Groups { get; } = new();
    public ObservableCollection<TrackScanInfo> SelectedGroupTracks { get; } = new();

    public Window? HostWindow { get; set; }

    public DuplicateFinderViewModel(AppSettings settings, Action<string> appendMainLog)
    {
        _settings = settings;
        _appendMainLog = appendMainLog;
    }

    partial void OnSelectedGroupChanged(DuplicateGroup? value)
    {
        SelectedGroupTracks.Clear();
        if (value == null) return;
        foreach (var t in value.Tracks)
            SelectedGroupTracks.Add(t);
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (HostWindow == null) return;
        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Scan folder for duplicate tracks",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            FolderPath = folders[0].TryGetLocalPath() ?? FolderPath;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            StatusText = "Select a valid folder.";
            return;
        }

        IsBusy = true;
        StatusText = "Scanning...";
        Groups.Clear();
        SelectedGroupTracks.Clear();
        _cts = new CancellationTokenSource();
        _scanRoot = Path.GetFullPath(FolderPath);

        try
        {
            var tracks = await Task.Run(
                () => DuplicateFinderService.ScanFolder(_scanRoot, msg => StatusText = msg, _cts.Token),
                _cts.Token);
            var groups = DuplicateFinderService.FindDuplicates(tracks, _settings);
            foreach (var g in groups)
                Groups.Add(g);
            StatusText = groups.Count == 0
                ? $"No likely duplicates among {tracks.Count} file(s)."
                : $"Found {groups.Count} duplicate group(s) among {tracks.Count} file(s).";
            _appendMainLog($"Duplicate Finder: {StatusText}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _appendMainLog($"Duplicate Finder error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void CopyPathsToLog()
    {
        var group = SelectedGroup;
        if (group == null)
        {
            StatusText = "Select a duplicate group first.";
            return;
        }

        _appendMainLog($"Duplicate group — {group.Reason}:");
        foreach (var t in group.Tracks)
            _appendMainLog($"  {t.Path}");
        StatusText = $"Copied {group.Tracks.Count} path(s) to main log.";
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var track = SelectedTrack ?? SelectedGroup?.Tracks.FirstOrDefault();
        if (track == null)
        {
            StatusText = "Select a track or group first.";
            return;
        }

        var dir = Path.GetDirectoryName(track.Path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            StatusText = "Folder not found.";
            return;
        }

        try
        {
            // Cross-platform best-effort: on Windows explorer, elsewhere xdg-open / open
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            StatusText = $"Opened: {dir}";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open folder: {ex.Message}";
            _appendMainLog($"Open folder failed: {dir} — {ex.Message}");
        }
    }

    [RelayCommand]
    private void QuarantineSelected()
    {
        var group = SelectedGroup;
        if (group == null)
        {
            StatusText = "Select a duplicate group first.";
            return;
        }

        if (!ConfirmQuarantine)
        {
            StatusText = "Check “I confirm quarantine” before moving files.";
            return;
        }

        if (string.IsNullOrEmpty(_scanRoot))
            _scanRoot = Path.GetFullPath(FolderPath);

        // Keep the first file; quarantine the rest (safer default)
        var toMove = group.Tracks.Skip(1).Select(t => t.Path).ToList();
        if (toMove.Count == 0)
        {
            StatusText = "Nothing to quarantine (group has one file).";
            return;
        }

        var moved = DuplicateFinderService.QuarantineFiles(toMove, _scanRoot, _appendMainLog);
        StatusText = $"Moved {moved.Count} file(s) to DuplicatesQuarantine (kept first).";
        ConfirmQuarantine = false;

        // Refresh group list entries
        foreach (var t in toMove)
        {
            var match = group.Tracks.FirstOrDefault(x =>
                string.Equals(x.Path, t, StringComparison.OrdinalIgnoreCase));
            if (match != null) group.Tracks.Remove(match);
        }
        OnSelectedGroupChanged(group);
        if (group.Tracks.Count < 2)
            Groups.Remove(group);
    }
}
