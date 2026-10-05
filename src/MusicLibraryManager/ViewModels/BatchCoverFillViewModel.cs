using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.ViewModels;

public partial class BatchCoverFillViewModel : ViewModelBase
{
    private readonly Action<string> _appendMainLog;
    private CancellationTokenSource? _cts;
    private MusicBrainzClient? _mbClient;

    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _statusText = "Pick an artist folder (containing album subfolders) and start.";
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";

    public Window? HostWindow { get; set; }

    public BatchCoverFillViewModel(Action<string> appendMainLog, string? initialFolder = null)
    {
        _appendMainLog = appendMainLog;
        if (!string.IsNullOrWhiteSpace(initialFolder))
            FolderPath = initialFolder;
    }

    private MusicBrainzClient GetMbClient() =>
        _mbClient ??= new MusicBrainzClient(msg => AppendLog(msg));

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (HostWindow == null) return;
        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select artist folder (album subfolders inside)",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            FolderPath = folders[0].TryGetLocalPath() ?? FolderPath;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            StatusText = "Select a valid artist / library folder.";
            return;
        }

        IsBusy = true;
        Progress = 0;
        ProgressText = "";
        LogText = string.Empty;
        StatusText = "Starting...";
        _cts = new CancellationTokenSource();
        var root = Path.GetFullPath(FolderPath);

        try
        {
            var summary = await Task.Run(async () =>
            {
                return await BatchCoverFillService.FillMissingCoversAsync(
                    root,
                    GetMbClient(),
                    msg =>
                    {
                        // Marshal log updates onto UI thread via Avalonia dispatcher if available
                        AppendLog(msg);
                    },
                    (current, total) =>
                    {
                        var pct = total <= 0 ? 0 : (100.0 * current / total);
                        Progress = pct;
                        ProgressText = $"{current} of {total}";
                        StatusText = $"Processing {current} of {total}...";
                    },
                    _cts.Token).ConfigureAwait(false);
            }, _cts.Token).ConfigureAwait(true);

            StatusText =
                $"Done — downloaded {summary.Downloaded}, skipped {summary.Skipped}, " +
                $"no match {summary.NoMatch}, failed {summary.Failed} (of {summary.Total}).";
            Progress = 100;
            ProgressText = $"{summary.Total} of {summary.Total}";
            _appendMainLog($"Batch cover fill: {StatusText}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
            AppendLog("Cancelled by user.");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
            _appendMainLog($"Batch cover fill error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClearLog() => LogText = string.Empty;

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (string.IsNullOrEmpty(LogText))
            LogText = line;
        else
            LogText += Environment.NewLine + line;

        // Cap log growth
        if (LogText.Length > 200_000)
        {
            var keep = LogText.AsSpan(LogText.Length - 150_000).ToString();
            var cut = keep.IndexOf('\n');
            LogText = cut >= 0 ? keep[(cut + 1)..] : keep;
        }
    }
}
