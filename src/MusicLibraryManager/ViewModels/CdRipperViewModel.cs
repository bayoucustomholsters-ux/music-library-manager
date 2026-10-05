using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicLibraryManager.Models;
using MusicLibraryManager.Services;
using MusicLibraryManager.Views;

namespace MusicLibraryManager.ViewModels;

public partial class CdRipperViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly Action<string> _appendMainLog;
    private readonly Func<MusicBrainzClient> _mbClient;
    private CancellationTokenSource? _cts;
    private CdToc? _toc;
    private string? _discId;
    private string? _selectedReleaseId;

    [ObservableProperty] private CdDriveInfo? _selectedDrive;
    [ObservableProperty] private string _statusText = "Insert an audio CD and click Refresh.";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private string _year = "";
    [ObservableProperty] private string _genre = "";
    [ObservableProperty] private string _coverPath = "";
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _discIdText = "";
    [ObservableProperty] private string _tocSummary = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private int _selectedFormatIndex;
    [ObservableProperty] private string _noDriveMessage = "";
    [ObservableProperty] private bool _showNoDriveMessage;

    public ObservableCollection<CdDriveInfo> Drives { get; } = new();
    public ObservableCollection<CdRipTrackItem> Tracks { get; } = new();

    public ObservableCollection<string> FormatOptions { get; } = new()
    {
        "MP3 (from Settings bitrate)",
        "FLAC",
        "Apple Lossless (ALAC / M4A)"
    };

    public Window? HostWindow { get; set; }

    public bool CanReadToc => !IsBusy && SelectedDrive != null;
    public bool CanLookup => !IsBusy && _toc != null && _toc.AudioTrackCount > 0;
    public bool CanRip => !IsBusy && Tracks.Any(t => t.IsSelected && t.IsAudio) && !string.IsNullOrWhiteSpace(OutputFolder);
    public bool CanDownloadCover => !IsBusy && (!string.IsNullOrWhiteSpace(_selectedReleaseId)
                                               || !string.IsNullOrWhiteSpace(Artist) || !string.IsNullOrWhiteSpace(Album));

    public CdRipperViewModel(
        AppSettings settings,
        Action<string> appendMainLog,
        OutputFormat currentFormat,
        Func<MusicBrainzClient> mbClient)
    {
        _settings = settings;
        _appendMainLog = appendMainLog;
        _mbClient = mbClient;
        SelectedFormatIndex = currentFormat switch
        {
            OutputFormat.Flac => 1,
            OutputFormat.Alac => 2,
            _ => 0
        };
        OutputFolder = CdDriveService.GetDefaultOutputFolder(settings);
        AppendLog("CD Ripper — Windows 11 audio CD → FLAC / MP3.");
        AppendLog("UseSourceFolderAsOutput does not apply to CDs; output is LastOutputFolder / Music library.");
        RefreshDrives();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanReadToc));
        OnPropertyChanged(nameof(CanLookup));
        OnPropertyChanged(nameof(CanRip));
        OnPropertyChanged(nameof(CanDownloadCover));
    }

    partial void OnSelectedDriveChanged(CdDriveInfo? value)
    {
        OnPropertyChanged(nameof(CanReadToc));
    }

    partial void OnOutputFolderChanged(string value)
    {
        OnPropertyChanged(nameof(CanRip));
    }

    partial void OnArtistChanged(string value) => OnPropertyChanged(nameof(CanDownloadCover));
    partial void OnAlbumChanged(string value) => OnPropertyChanged(nameof(CanDownloadCover));

    [RelayCommand]
    private void RefreshDrives()
    {
        Drives.Clear();
        Tracks.Clear();
        _toc = null;
        _discId = null;
        DiscIdText = "";
        TocSummary = "";
        var drives = CdDriveService.ListDrives();
        foreach (var d in drives)
            Drives.Add(d);

        if (drives.Count == 0)
        {
            ShowNoDriveMessage = true;
            NoDriveMessage =
                "No optical drive was found. Connect a USB or internal CD/DVD drive, insert an audio CD, and click Refresh. " +
                "CD ripping is supported on Windows 11 (the published win-x64 app). Virtual CD drives may work if Windows reports them as CD-ROM.";
            StatusText = "No optical drive found.";
            AppendLog("No optical drive found.");
            SelectedDrive = null;
        }
        else
        {
            ShowNoDriveMessage = false;
            NoDriveMessage = "";
            SelectedDrive = drives.FirstOrDefault(d => d.IsReady) ?? drives[0];
            StatusText = $"{drives.Count} optical drive(s). Insert an audio CD and click Read TOC.";
            AppendLog($"Found {drives.Count} optical drive(s).");
        }

        OnPropertyChanged(nameof(CanReadToc));
        OnPropertyChanged(nameof(CanRip));
    }

    [RelayCommand]
    private async Task ReadTocAsync()
    {
        if (IsBusy) return;
        var drive = SelectedDrive;
        if (drive == null)
        {
            StatusText = "Select a drive first.";
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            StatusText = "CD TOC/rip requires Windows.";
            AppendLog("This process is not running on Windows. Use the published win-x64 build on Windows 11.");
            return;
        }

        IsBusy = true;
        StatusText = "Reading TOC…";
        Tracks.Clear();
        _toc = null;
        _discId = null;
        DiscIdText = "";
        try
        {
            var toc = await Task.Run(() => CdDriveService.ReadToc(drive));
            _toc = toc;
            var audio = toc.Tracks.Where(t => t.IsAudio).ToList();
            if (audio.Count == 0)
            {
                StatusText = "No audio tracks on this disc (data CD?).";
                TocSummary = "No audio tracks.";
                AppendLog("TOC has no audio tracks.");
                return;
            }

            foreach (var t in audio)
            {
                Tracks.Add(new CdRipTrackItem
                {
                    Number = t.Number,
                    Title = $"Track {t.Number:00}",
                    Length = t.Length,
                    StartLba = t.StartLba,
                    LengthSectors = t.LengthSectors,
                    IsAudio = true,
                    IsSelected = true
                });
            }

            try
            {
                _discId = DiscIdCalculator.Compute(toc);
                DiscIdText = _discId;
            }
            catch (Exception ex)
            {
                DiscIdText = "";
                AppendLog($"DiscID failed: {ex.Message}");
            }

            var dataCount = toc.Tracks.Count(t => !t.IsAudio);
            TocSummary = dataCount > 0
                ? $"{audio.Count} audio track(s), {dataCount} data track(s) skipped · {FormatLength(toc.TotalAudioLength)}"
                : $"{audio.Count} audio track(s) · {FormatLength(toc.TotalAudioLength)}";

            if (string.IsNullOrWhiteSpace(Album) && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                Album = drive.VolumeLabel!;

            StatusText = $"TOC: {audio.Count} audio track(s). DiscID {_discId}";
            AppendLog($"TOC: {TocSummary}");
            if (!string.IsNullOrEmpty(_discId))
                AppendLog($"DiscID: {_discId}");
        }
        catch (Exception ex)
        {
            StatusText = $"Could not read TOC: {ex.Message}";
            AppendLog($"TOC error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanLookup));
            OnPropertyChanged(nameof(CanRip));
        }
    }

    [RelayCommand]
    private async Task LookupDiscIdAsync()
    {
        if (IsBusy || _toc == null) return;

        IsBusy = true;
        StatusText = "Looking up MusicBrainz DiscID…";
        try
        {
            var client = _mbClient();
            IReadOnlyList<MusicBrainzReleaseCandidate> results = Array.Empty<MusicBrainzReleaseCandidate>();
            if (!string.IsNullOrEmpty(_discId))
                results = await client.LookupDiscIdAsync(_discId);

            if (results.Count == 0)
            {
                var tocQuery = DiscIdCalculator.ToTocQuery(_toc);
                AppendLog("No exact DiscID match — trying TOC lookup.");
                results = await client.LookupByTocAsync(tocQuery);
            }

            if (results.Count == 0)
            {
                StatusText = "No MusicBrainz match for this disc.";
                AppendLog("MusicBrainz: no DiscID / TOC match. Enter artist/album/titles manually, or use Lookup by name.");
                return;
            }

            MusicBrainzReleaseCandidate? chosen = null;
            if (HostWindow != null && results.Count > 0)
            {
                var picker = new ReleasePickerWindow(results);
                chosen = await picker.ShowDialog<MusicBrainzReleaseCandidate?>(HostWindow);
            }
            else
            {
                chosen = results[0];
            }

            if (chosen == null)
            {
                StatusText = "Lookup cancelled.";
                return;
            }

            var detail = chosen.Tracks.Count > 0
                ? chosen
                : await client.GetReleaseDetailsAsync(chosen.ReleaseId) ?? chosen;
            ApplyRelease(detail);
            StatusText = $"Applied: {detail.Artist} — {detail.Album} ({detail.TrackCount} tracks)";
        }
        catch (Exception ex)
        {
            StatusText = $"MusicBrainz error: {ex.Message}";
            AppendLog($"MusicBrainz error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LookupByNameAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Artist) && string.IsNullOrWhiteSpace(Album))
        {
            StatusText = "Enter artist and/or album to search by name.";
            return;
        }

        IsBusy = true;
        StatusText = "Searching MusicBrainz…";
        try
        {
            var client = _mbClient();
            var results = await client.SearchReleasesAsync(Artist, Album);
            if (results.Count == 0)
            {
                StatusText = "No MusicBrainz releases found.";
                return;
            }

            MusicBrainzReleaseCandidate? chosen = null;
            if (HostWindow != null)
            {
                var picker = new ReleasePickerWindow(results);
                chosen = await picker.ShowDialog<MusicBrainzReleaseCandidate?>(HostWindow);
            }
            else chosen = results[0];

            if (chosen == null)
            {
                StatusText = "Lookup cancelled.";
                return;
            }

            var detail = await client.GetReleaseDetailsAsync(chosen.ReleaseId) ?? chosen;
            ApplyRelease(detail);
            StatusText = $"Applied: {detail.Artist} — {detail.Album}";
        }
        catch (Exception ex)
        {
            StatusText = $"MusicBrainz error: {ex.Message}";
            AppendLog($"MusicBrainz error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyRelease(MusicBrainzReleaseCandidate release)
    {
        if (!string.IsNullOrWhiteSpace(release.Artist)) Artist = release.Artist;
        if (!string.IsNullOrWhiteSpace(release.Album)) Album = release.Album;
        if (!string.IsNullOrWhiteSpace(release.Year)) Year = release.Year!;
        if (!string.IsNullOrWhiteSpace(release.Genre) && string.IsNullOrWhiteSpace(Genre))
            Genre = release.Genre!;
        _selectedReleaseId = release.ReleaseId;
        OnPropertyChanged(nameof(CanDownloadCover));

        if (release.Tracks.Count > 0)
        {
            foreach (var item in Tracks)
            {
                var match = release.Tracks.FirstOrDefault(t => t.Number == item.Number)
                            ?? (item.Number - 1 < release.Tracks.Count ? release.Tracks[item.Number - 1] : null);
                if (match != null && !string.IsNullOrWhiteSpace(match.Title))
                    item.Title = match.Title;
            }
        }

        AppendLog($"Applied: {release.Artist} — {release.Album} ({release.Year}) [{release.ReleaseId}]");
    }

    [RelayCommand]
    private async Task DownloadCoverAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Downloading cover…";
        try
        {
            var client = _mbClient();
            var releaseId = _selectedReleaseId;
            if (string.IsNullOrWhiteSpace(releaseId))
            {
                var results = await client.SearchReleasesAsync(Artist, Album);
                if (results.Count == 0)
                {
                    StatusText = "No release found for cover download.";
                    return;
                }
                if (HostWindow != null && results.Count > 1)
                {
                    var picker = new ReleasePickerWindow(results);
                    var chosen = await picker.ShowDialog<MusicBrainzReleaseCandidate?>(HostWindow);
                    if (chosen == null)
                    {
                        StatusText = "Cover download cancelled.";
                        return;
                    }
                    releaseId = chosen.ReleaseId;
                }
                else
                {
                    releaseId = results[0].ReleaseId;
                }
                _selectedReleaseId = releaseId;
            }

            var destDir = !string.IsNullOrWhiteSpace(OutputFolder) ? OutputFolder : Path.GetTempPath();
            Directory.CreateDirectory(destDir);
            var destPath = Path.Combine(destDir, "cover.jpg");
            var saved = await client.DownloadFrontCoverAsync(releaseId!, destPath);
            if (saved != null)
            {
                CoverPath = saved;
                StatusText = "Cover downloaded.";
                AppendLog($"Cover: {saved}");
            }
            else
            {
                StatusText = "No cover art for this release.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Cover error: {ex.Message}";
            AppendLog($"Cover error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in Tracks)
            t.IsSelected = true;
        OnPropertyChanged(nameof(CanRip));
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var t in Tracks)
            t.IsSelected = false;
        OnPropertyChanged(nameof(CanRip));
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (HostWindow == null) return;
        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "CD rip output folder",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            OutputFolder = folders[0].TryGetLocalPath() ?? OutputFolder;
            _settings.LastOutputFolder = OutputFolder;
            SettingsService.Save(_settings);
        }
    }

    [RelayCommand]
    private async Task BrowseCoverAsync()
    {
        if (HostWindow == null) return;
        var files = await HostWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Cover art",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png" } },
                FilePickerFileTypes.All
            }
        });
        if (files.Count > 0)
            CoverPath = files[0].TryGetLocalPath() ?? CoverPath;
    }

    [RelayCommand]
    private async Task RipAsync()
    {
        if (IsBusy) return;
        var drive = SelectedDrive;
        if (drive == null || _toc == null)
        {
            StatusText = "Read the TOC first.";
            return;
        }

        var selected = Tracks.Where(t => t.IsSelected && t.IsAudio).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Select at least one audio track.";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            StatusText = "Choose an output folder.";
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            StatusText = "CD ripping requires Windows 11 (win-x64 build).";
            AppendLog("Native CDDA is Windows-only.");
            return;
        }

        Directory.CreateDirectory(OutputFolder);
        _settings.LastOutputFolder = OutputFolder;
        SettingsService.Save(_settings);

        var format = SelectedFormatIndex switch
        {
            1 => OutputFormat.Flac,
            2 => OutputFormat.Alac,
            _ => OutputFormat.Mp3
        };
        IsBusy = true;
        Progress = 0;
        StatusText = "Ripping…";
        _cts = new CancellationTokenSource();
        foreach (var t in selected)
            t.Status = "";

        try
        {
            var ripper = new CdRipperService(_settings, AppendLog, p => Progress = p * 100);
            var albumDir = await ripper.RipAsync(
                drive,
                _toc,
                selected,
                OutputFolder,
                format,
                Artist,
                Album,
                Year,
                Genre,
                string.IsNullOrWhiteSpace(CoverPath) ? null : CoverPath,
                _cts.Token);
            StatusText = $"Done — {selected.Count} track(s) → {albumDir}";
            Progress = 100;
            _appendMainLog($"CD rip complete: {albumDir}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
            AppendLog("Rip cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
            _appendMainLog($"CD rip error: {ex.Message}");
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
    private void ClearLog() => LogText = string.Empty;

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText += line + Environment.NewLine;
    }

    private static string FormatLength(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
