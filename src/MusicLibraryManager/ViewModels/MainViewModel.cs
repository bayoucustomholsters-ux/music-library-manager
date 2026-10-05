using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicLibraryManager.Models;
using MusicLibraryManager.Services;
using MusicLibraryManager.Views;

namespace MusicLibraryManager.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private AppSettings _settings;
    private CancellationTokenSource? _cts;
    private List<MusicBrainzTrackInfo>? _lookupTracks;
    private string? _selectedReleaseId;
    private MusicBrainzClient? _mbClient;
    private WatchFolderService? _watchService;

    [ObservableProperty] private string _audioPath = string.Empty;
    [ObservableProperty] private string _cuePath = string.Empty;
    [ObservableProperty] private string _coverPath = string.Empty;
    [ObservableProperty] private string _trackListPath = string.Empty;
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private string _batchFolder = string.Empty;
    [ObservableProperty] private string _convertFolder = string.Empty;
    [ObservableProperty] private string _watchFolderPath = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _album = string.Empty;
    [ObservableProperty] private string _year = string.Empty;
    [ObservableProperty] private string _genre = string.Empty;
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _selectedFormatIndex;
    [ObservableProperty] private string _ffmpegStatus = "Checking ffmpeg...";
    [ObservableProperty] private bool _isBatchMode;
    [ObservableProperty] private string _lookupStatus = string.Empty;
    [ObservableProperty] private bool _isWatchRunning;
    [ObservableProperty] private string _watchStatus = "Watch: stopped";

    public ObservableCollection<string> FormatOptions { get; } = new()
    {
        "MP3 320 CBR (default)",
        "FLAC",
        "Apple Lossless (ALAC / M4A)",
        "Keep original"
    };

    public Window? HostWindow { get; set; }

    public bool CanLookupMetadata =>
        !IsBusy && (!string.IsNullOrWhiteSpace(Artist) || !string.IsNullOrWhiteSpace(Album)
                    || !string.IsNullOrWhiteSpace(AudioPath));

    public bool CanIdentifyWithAcoustId =>
        !IsBusy && !string.IsNullOrWhiteSpace(AudioPath);

    public bool CanLookupDiscogs =>
        !IsBusy && (!string.IsNullOrWhiteSpace(Artist) || !string.IsNullOrWhiteSpace(Album)
                    || !string.IsNullOrWhiteSpace(AudioPath));

    public bool CanDownloadCover =>
        !IsBusy && (!string.IsNullOrWhiteSpace(_selectedReleaseId)
                    || !string.IsNullOrWhiteSpace(Artist) || !string.IsNullOrWhiteSpace(Album));

    public MainViewModel()
    {
        _settings = SettingsService.Load();
        ApplyOutputFolderFromSettings();
        WatchFolderPath = _settings.WatchFolderPath ?? "";
        SelectedFormatIndex = _settings.DefaultFormat switch
        {
            OutputFormat.Flac => 1,
            OutputFormat.Alac => 2,
            OutputFormat.KeepOriginal => 3,
            _ => 0
        };
        RefreshFfmpegStatus();
        AppendLog("Music Library Manager v5.1.0 — Phase 5.1");
        AppendLog("Drop an album audio file or use Browse. Optional: CUE, cover, track list.");
        AppendLog("Phase 5.1: Batch fill missing covers · CD Ripper · ALAC · AcoustID · Discogs · ReplayGain.");
        if (_settings.UseSourceFolderAsOutput)
            AppendLog("Output default: same folder as source album file (change in Settings).");

        if (_settings.WatchFolderEnabled
            && !string.IsNullOrWhiteSpace(_settings.WatchFolderPath)
            && Directory.Exists(_settings.WatchFolderPath))
        {
            try { StartWatchInternal(); }
            catch (Exception ex) { AppendLog($"Could not auto-start watch: {ex.Message}"); }
        }
    }

    private MusicBrainzClient GetMbClient() =>
        _mbClient ??= new MusicBrainzClient(AppendLog);

    private OutputFormat CurrentFormat => SelectedFormatIndex switch
    {
        1 => OutputFormat.Flac,
        2 => OutputFormat.Alac,
        3 => OutputFormat.KeepOriginal,
        _ => OutputFormat.Mp3
    };

    private void ApplyOutputFolderFromSettings()
    {
        if (!_settings.UseSourceFolderAsOutput && !string.IsNullOrWhiteSpace(_settings.LastOutputFolder))
            OutputFolder = _settings.LastOutputFolder!;
    }

    public void RefreshFfmpegStatus()
    {
        var path = FfmpegLocator.FindFfmpeg(_settings.FfmpegPath);
        FfmpegStatus = path != null
            ? $"ffmpeg: {path}"
            : "ffmpeg: NOT FOUND — place ffmpeg.exe next to the app or in ffmpeg\\ subfolder";
    }

    partial void OnAudioPathChanged(string value)
    {
        SyncOutputFolderFromAudio(value);
        OnPropertyChanged(nameof(CanLookupMetadata));
        OnPropertyChanged(nameof(CanDownloadCover));
        OnPropertyChanged(nameof(CanIdentifyWithAcoustId));
        OnPropertyChanged(nameof(CanLookupDiscogs));
    }

    partial void OnArtistChanged(string value)
    {
        OnPropertyChanged(nameof(CanLookupMetadata));
        OnPropertyChanged(nameof(CanDownloadCover));
        OnPropertyChanged(nameof(CanLookupDiscogs));
    }

    partial void OnAlbumChanged(string value)
    {
        OnPropertyChanged(nameof(CanLookupMetadata));
        OnPropertyChanged(nameof(CanDownloadCover));
        OnPropertyChanged(nameof(CanLookupDiscogs));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLookupMetadata));
        OnPropertyChanged(nameof(CanDownloadCover));
        OnPropertyChanged(nameof(CanIdentifyWithAcoustId));
        OnPropertyChanged(nameof(CanLookupDiscogs));
    }

    private void SyncOutputFolderFromAudio(string audioPath)
    {
        if (!_settings.UseSourceFolderAsOutput) return;
        if (string.IsNullOrWhiteSpace(audioPath)) return;
        var dir = Path.GetDirectoryName(audioPath);
        if (!string.IsNullOrEmpty(dir))
            OutputFolder = dir;
    }

    private async Task AutoLookupSafeAsync()
    {
        try
        {
            await LookupMetadataAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"Auto-lookup skipped: {ex.Message}");
        }
    }

    public void ApplyDroppedFiles(IEnumerable<string> paths)
    {
        var loadedAudio = false;
        foreach (var path in paths)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (AlbumSplitter.AudioExtensions.Contains(ext))
            {
                AudioPath = path;
                var job = AlbumSplitter.DiscoverFromAudio(path);
                if (job != null)
                {
                    if (!string.IsNullOrEmpty(job.CuePath)) CuePath = job.CuePath!;
                    if (!string.IsNullOrEmpty(job.CoverPath)) CoverPath = job.CoverPath!;
                    if (!string.IsNullOrEmpty(job.TrackListPath)) TrackListPath = job.TrackListPath!;
                    if (!string.IsNullOrEmpty(job.Artist)) Artist = job.Artist!;
                    if (!string.IsNullOrEmpty(job.Album)) Album = job.Album!;
                    if (!string.IsNullOrEmpty(job.Year)) Year = job.Year!;
                    if (!string.IsNullOrEmpty(job.Genre)) Genre = job.Genre!;
                }

                if (string.IsNullOrWhiteSpace(Artist) || string.IsNullOrWhiteSpace(Album))
                {
                    var (guessArtist, guessAlbum) = MusicBrainzClient.GuessFromFilename(path);
                    if (string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(guessArtist))
                        Artist = guessArtist!;
                    if (string.IsNullOrWhiteSpace(Album) && !string.IsNullOrWhiteSpace(guessAlbum))
                        Album = guessAlbum!;
                }

                AppendLog($"Audio: {Path.GetFileName(path)}");
                loadedAudio = true;
            }
            else if (ext == ".cue")
            {
                CuePath = path;
                TryLoadCueMetadata(path);
                AppendLog($"CUE: {Path.GetFileName(path)}");
            }
            else if (ext is ".jpg" or ".jpeg" or ".png")
            {
                CoverPath = path;
                AppendLog($"Cover: {Path.GetFileName(path)}");
            }
            else if (ext == ".txt")
            {
                TrackListPath = path;
                AppendLog($"Track list: {Path.GetFileName(path)}");
            }
        }

        if (loadedAudio && _settings.AutoLookupOnLoad)
            _ = AutoLookupSafeAsync();
    }

    private void TryLoadCueMetadata(string path)
    {
        try
        {
            var cue = CueParser.ParseFile(path);
            if (!string.IsNullOrWhiteSpace(cue.Performer)) Artist = cue.Performer;
            if (!string.IsNullOrWhiteSpace(cue.Title)) Album = cue.Title;
            if (!string.IsNullOrWhiteSpace(cue.Date)) Year = cue.Date;
            if (!string.IsNullOrWhiteSpace(cue.Genre)) Genre = cue.Genre;
        }
        catch (Exception ex)
        {
            AppendLog($"CUE parse warning: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BrowseAudioAsync()
    {
        var file = await PickFileAsync("Select album audio",
            new FilePickerFileType("Audio")
            {
                Patterns = new[] { "*.mp3", "*.flac", "*.wav", "*.ape", "*.m4a", "*.wma", "*.aiff", "*.aif", "*.ogg", "*.oga" }
            });
        if (file != null)
            ApplyDroppedFiles(new[] { file });
    }

    [RelayCommand]
    private async Task BrowseCueAsync()
    {
        var file = await PickFileAsync("Select CUE sheet",
            new FilePickerFileType("CUE") { Patterns = new[] { "*.cue" } });
        if (file != null)
        {
            CuePath = file;
            TryLoadCueMetadata(file);
        }
    }

    [RelayCommand]
    private async Task BrowseCoverAsync()
    {
        var file = await PickFileAsync("Select cover art",
            new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png" } });
        if (file != null) CoverPath = file;
    }

    [RelayCommand]
    private async Task BrowseTrackListAsync()
    {
        var file = await PickFileAsync("Select track list",
            new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } });
        if (file != null) TrackListPath = file;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (HostWindow == null) return;
        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Output folder",
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
    private async Task BrowseBatchFolderAsync()
    {
        if (HostWindow == null) return;
        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Batch albums folder",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            BatchFolder = folders[0].TryGetLocalPath() ?? BatchFolder;
    }

    [RelayCommand]
    private async Task BrowseConvertFolderAsync()
    {
        if (HostWindow == null) return;
        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Folder of already-split tracks",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            ConvertFolder = folders[0].TryGetLocalPath() ?? ConvertFolder;
    }

    [RelayCommand]
    private async Task BrowseWatchFolderAsync()
    {
        if (HostWindow == null) return;
        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Watch folder (Albums To Split)",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            WatchFolderPath = folders[0].TryGetLocalPath() ?? WatchFolderPath;
            _settings.WatchFolderPath = WatchFolderPath;
            SettingsService.Save(_settings);
        }
    }

    [RelayCommand]
    private async Task LookupMetadataAsync()
    {
        if (IsBusy) return;

        var artist = Artist;
        var album = Album;
        if ((string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            && !string.IsNullOrWhiteSpace(AudioPath))
        {
            var (ga, gal) = MusicBrainzClient.GuessFromFilename(AudioPath);
            if (string.IsNullOrWhiteSpace(artist)) artist = ga ?? "";
            if (string.IsNullOrWhiteSpace(album)) album = gal ?? "";
        }

        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(album))
        {
            AppendLog("ERROR: Enter artist and/or album (or load an audio file named \"Artist - Album\").");
            return;
        }

        IsBusy = true;
        StatusText = "Looking up MusicBrainz...";
        LookupStatus = "Searching...";
        try
        {
            var client = GetMbClient();
            var results = await client.SearchReleasesAsync(artist, album);
            if (results.Count == 0)
            {
                AppendLog("MusicBrainz: no releases found.");
                LookupStatus = "No results";
                StatusText = "Ready";
                return;
            }

            AppendLog($"MusicBrainz: {results.Count} candidate(s).");
            MusicBrainzReleaseCandidate? chosen = null;

            if (HostWindow != null)
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
                AppendLog("Lookup cancelled.");
                LookupStatus = "Cancelled";
                StatusText = "Ready";
                return;
            }

            var detail = await client.GetReleaseDetailsAsync(chosen.ReleaseId) ?? chosen;
            ApplyRelease(detail);
            LookupStatus = $"Selected: {detail.Album} ({detail.TrackCount} tracks)";
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            AppendLog($"MusicBrainz error: {ex.Message}");
            LookupStatus = "Error";
            StatusText = "Error";
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
        _lookupTracks = release.Tracks.Count > 0 ? release.Tracks.ToList() : null;

        if (_lookupTracks is { Count: > 0 })
        {
            var dir = !string.IsNullOrWhiteSpace(AudioPath)
                ? Path.GetDirectoryName(AudioPath) ?? Path.GetTempPath()
                : Path.GetTempPath();
            var tempPath = Path.Combine(dir, $".mlm-mb-tracks-{release.ReleaseId[..8]}.txt");
            try
            {
                File.WriteAllLines(tempPath,
                    _lookupTracks.Select(t => $"{t.Number:00}. {t.Title}"));
                TrackListPath = tempPath;
                AppendLog($"Track list from MusicBrainz: {_lookupTracks.Count} titles → {Path.GetFileName(tempPath)}");
            }
            catch (Exception ex)
            {
                AppendLog($"Could not write temp track list: {ex.Message} (using in-memory list).");
            }
        }

        AppendLog($"Applied: {release.Artist} — {release.Album} ({release.Year}) [{release.ReleaseId}]");
        OnPropertyChanged(nameof(CanDownloadCover));
    }

    
    [RelayCommand]
    private async Task IdentifyWithAcoustIdAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
        {
            AppendLog("ERROR: Select an audio file to fingerprint.");
            return;
        }

        var apiKey = _settings.AcoustIdApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AppendLog("ERROR: AcoustID API key not set. Get a free key at https://acoustid.org/api and enter it in Settings.");
            LookupStatus = "AcoustID key required";
            return;
        }

        var fpcalc = FpcalcLocator.FindFpcalc(_settings.FpcalcPath);
        if (fpcalc == null)
        {
            AppendLog("ERROR: fpcalc.exe not found. Place it under fpcalc\\ or ffmpeg\\ next to the app, or set the path in Settings.");
            AppendLog("Download: https://github.com/acoustid/chromaprint/releases (Windows x86_64 fpcalc zip).");
            LookupStatus = "fpcalc missing";
            return;
        }

        IsBusy = true;
        StatusText = "Fingerprinting with AcoustID...";
        LookupStatus = "Fingerprinting...";
        try
        {
            using var acoustId = new AcoustIdClient(AppendLog);
            var (duration, fingerprint) = await acoustId.FingerprintAsync(AudioPath, fpcalc);
            var lookup = await acoustId.LookupAsync(apiKey, duration, fingerprint);
            var candidates = AcoustIdClient.ToMusicBrainzCandidates(lookup);
            if (candidates.Count == 0)
            {
                AppendLog("AcoustID: no MusicBrainz release matches.");
                LookupStatus = "No AcoustID match";
                StatusText = "Ready";
                return;
            }

            AppendLog($"AcoustID: {candidates.Count} release candidate(s).");
            MusicBrainzReleaseCandidate? chosen = null;
            if (HostWindow != null)
            {
                var picker = new ReleasePickerWindow(candidates);
                chosen = await picker.ShowDialog<MusicBrainzReleaseCandidate?>(HostWindow);
            }
            else
            {
                chosen = candidates[0];
            }

            if (chosen == null)
            {
                AppendLog("AcoustID lookup cancelled.");
                LookupStatus = "Cancelled";
                StatusText = "Ready";
                return;
            }

            var client = GetMbClient();
            var detail = await client.GetReleaseDetailsAsync(chosen.ReleaseId) ?? chosen;
            ApplyRelease(detail);
            LookupStatus = $"AcoustID → {detail.Album} ({detail.TrackCount} tracks)";
            StatusText = "Ready";

            // Offer cover download using existing flow
            if (HostWindow != null && !string.IsNullOrWhiteSpace(detail.ReleaseId))
            {
                try
                {
                    var destDir = Path.GetDirectoryName(AudioPath) ?? Path.GetTempPath();
                    var destPath = Path.Combine(destDir, "cover.jpg");
                    var saved = await client.DownloadFrontCoverAsync(detail.ReleaseId, destPath);
                    if (saved != null)
                    {
                        CoverPath = saved;
                        AppendLog($"Cover ready: {saved}");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Cover download skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"AcoustID error: {ex.Message}");
            LookupStatus = "Error";
            StatusText = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LookupDiscogsAsync()
    {
        if (IsBusy) return;

        var token = _settings.DiscogsToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            AppendLog("ERROR: Discogs token not set. Create a personal access token at https://www.discogs.com/settings/developers and enter it in Settings.");
            LookupStatus = "Discogs token required";
            return;
        }

        var artist = Artist;
        var album = Album;
        if ((string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            && !string.IsNullOrWhiteSpace(AudioPath))
        {
            var (ga, gal) = MusicBrainzClient.GuessFromFilename(AudioPath);
            if (string.IsNullOrWhiteSpace(artist)) artist = ga ?? "";
            if (string.IsNullOrWhiteSpace(album)) album = gal ?? "";
        }

        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(album))
        {
            AppendLog("ERROR: Enter artist and/or album for Discogs search.");
            return;
        }

        IsBusy = true;
        StatusText = "Looking up Discogs...";
        LookupStatus = "Discogs searching...";
        try
        {
            using var discogs = new DiscogsClient(_settings.DiscogsUserAgent, AppendLog);
            var results = await discogs.SearchReleasesAsync(token, artist, album);
            if (results.Count == 0)
            {
                AppendLog("Discogs: no releases found.");
                LookupStatus = "No Discogs results";
                StatusText = "Ready";
                return;
            }

            AppendLog($"Discogs: {results.Count} candidate(s).");
            DiscogsReleaseCandidate? chosen = null;
            if (HostWindow != null)
            {
                var picker = new DiscogsPickerWindow(results);
                chosen = await picker.ShowDialog<DiscogsReleaseCandidate?>(HostWindow);
            }
            else
            {
                chosen = results[0];
            }

            if (chosen == null)
            {
                AppendLog("Discogs lookup cancelled.");
                LookupStatus = "Cancelled";
                StatusText = "Ready";
                return;
            }

            var detail = await discogs.GetReleaseAsync(token, chosen.Id) ?? chosen;
            ApplyDiscogsRelease(detail);
            LookupStatus = $"Discogs: {detail.Album} ({detail.Tracks.Count} tracks)";
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            AppendLog($"Discogs error: {ex.Message}");
            LookupStatus = "Error";
            StatusText = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDiscogsRelease(DiscogsReleaseCandidate release)
    {
        if (!string.IsNullOrWhiteSpace(release.Artist)) Artist = release.Artist;
        if (!string.IsNullOrWhiteSpace(release.Album)) Album = release.Album;
        if (!string.IsNullOrWhiteSpace(release.Year)) Year = release.Year!;
        var genre = !string.IsNullOrWhiteSpace(release.Genre) ? release.Genre : release.Style;
        if (!string.IsNullOrWhiteSpace(genre) && string.IsNullOrWhiteSpace(Genre))
            Genre = genre!;

        if (release.Tracks.Count > 0)
        {
            _lookupTracks = release.Tracks.Select(t => new MusicBrainzTrackInfo
            {
                Number = t.Number,
                Title = t.Title
            }).ToList();

            var dir = !string.IsNullOrWhiteSpace(AudioPath)
                ? Path.GetDirectoryName(AudioPath) ?? Path.GetTempPath()
                : Path.GetTempPath();
            var tempPath = Path.Combine(dir, $".mlm-discogs-tracks-{release.Id}.txt");
            try
            {
                File.WriteAllLines(tempPath,
                    _lookupTracks.Select(t => $"{t.Number:00}. {t.Title}"));
                TrackListPath = tempPath;
                AppendLog($"Track list from Discogs: {_lookupTracks.Count} titles → {Path.GetFileName(tempPath)}");
            }
            catch (Exception ex)
            {
                AppendLog($"Could not write temp track list: {ex.Message}");
            }
        }

        AppendLog($"Applied Discogs: {release.Artist} — {release.Album} ({release.Year}) [#{release.Id}]");
    }


[RelayCommand]
    private async Task DownloadCoverAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Downloading cover...";
        try
        {
            var client = GetMbClient();
            var releaseId = _selectedReleaseId;

            if (string.IsNullOrWhiteSpace(releaseId))
            {
                var results = await client.SearchReleasesAsync(Artist, Album);
                if (results.Count == 0)
                {
                    AppendLog("No release found for cover download.");
                    StatusText = "Ready";
                    return;
                }

                if (HostWindow != null && results.Count > 1)
                {
                    var picker = new ReleasePickerWindow(results);
                    var chosen = await picker.ShowDialog<MusicBrainzReleaseCandidate?>(HostWindow);
                    if (chosen == null)
                    {
                        AppendLog("Cover download cancelled.");
                        StatusText = "Ready";
                        return;
                    }
                    releaseId = chosen.ReleaseId;
                    _selectedReleaseId = releaseId;
                }
                else
                {
                    releaseId = results[0].ReleaseId;
                    _selectedReleaseId = releaseId;
                }
            }

            var destDir = !string.IsNullOrWhiteSpace(AudioPath)
                ? Path.GetDirectoryName(AudioPath) ?? Path.GetTempPath()
                : (!string.IsNullOrWhiteSpace(OutputFolder) ? OutputFolder : Path.GetTempPath());
            var destPath = Path.Combine(destDir, "cover.jpg");

            var saved = await client.DownloadFrontCoverAsync(releaseId!, destPath);
            if (saved != null)
            {
                CoverPath = saved;
                AppendLog($"Cover ready: {saved}");
            }
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            AppendLog($"Cover download error: {ex.Message}");
            StatusText = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SplitAlbumAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
        {
            AppendLog("ERROR: Select a valid audio file.");
            return;
        }

        if (_settings.UseSourceFolderAsOutput)
            SyncOutputFolderFromAudio(AudioPath);

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            AppendLog("ERROR: Select an output folder (or enable same-as-source in Settings).");
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusText = "Splitting...";
        _cts = new CancellationTokenSource();
        var format = CurrentFormat;

        try
        {
            var job = new AlbumJob
            {
                AudioPath = AudioPath,
                CuePath = string.IsNullOrWhiteSpace(CuePath) ? null : CuePath,
                CoverPath = string.IsNullOrWhiteSpace(CoverPath) ? null : CoverPath,
                TrackListPath = string.IsNullOrWhiteSpace(TrackListPath) ? null : TrackListPath,
                Artist = Artist,
                Album = Album,
                Year = Year,
                Genre = Genre,
                LookupTracks = _lookupTracks,
                MusicBrainzReleaseId = _selectedReleaseId
            };

            var splitter = new AlbumSplitter(_settings, AppendLog, p => Progress = p * 100);
            await Task.Run(() => splitter.ProcessAlbumAsync(job, OutputFolder, format, _cts.Token));
            StatusText = job.Status == "Skipped" ? "Skipped (already done)" : "Done";
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Error";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task RunBatchAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(BatchFolder) || !Directory.Exists(BatchFolder))
        {
            AppendLog("ERROR: Select a valid batch folder.");
            return;
        }
        if (!_settings.UseSourceFolderAsOutput && string.IsNullOrWhiteSpace(OutputFolder))
        {
            AppendLog("ERROR: Select an output folder.");
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusText = "Batch processing...";
        _cts = new CancellationTokenSource();
        var format = CurrentFormat;

        try
        {
            var jobs = AlbumSplitter.DiscoverBatch(BatchFolder);
            AppendLog($"Batch: found {jobs.Count} album(s).");
            var splitter = new AlbumSplitter(_settings, AppendLog, null);
            for (var i = 0; i < jobs.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                StatusText = $"Batch {i + 1}/{jobs.Count}";
                var outRoot = _settings.UseSourceFolderAsOutput
                    ? Path.GetDirectoryName(jobs[i].AudioPath) ?? OutputFolder
                    : OutputFolder;
                await Task.Run(() => splitter.ProcessAlbumAsync(jobs[i], outRoot, format, _cts.Token));
                Progress = (i + 1) * 100.0 / jobs.Count;
            }
            StatusText = "Batch done";
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            AppendLog("Batch cancelled.");
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Error";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task ConvertTracksFolderAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(ConvertFolder) || !Directory.Exists(ConvertFolder))
        {
            AppendLog("ERROR: Select a valid folder of tracks to convert.");
            return;
        }
        if (!_settings.UseSourceFolderAsOutput && string.IsNullOrWhiteSpace(OutputFolder))
        {
            AppendLog("ERROR: Select an output folder (or enable same-as-source in Settings).");
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusText = "Converting tracks...";
        _cts = new CancellationTokenSource();
        var format = CurrentFormat;

        try
        {
            var converter = new TrackFolderConverter(_settings, AppendLog, p => Progress = p * 100);
            var outRoot = _settings.UseSourceFolderAsOutput ? null : OutputFolder;
            await Task.Run(() => converter.ConvertFolderAsync(ConvertFolder, outRoot, format, _cts.Token));
            StatusText = "Convert done";
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            AppendLog("Convert cancelled.");
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Error";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private void StartWatch()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(WatchFolderPath))
            {
                AppendLog("ERROR: Set a watch folder path first.");
                return;
            }
            StartWatchInternal();
        }
        catch (Exception ex)
        {
            AppendLog($"Watch start failed: {ex.Message}");
            WatchStatus = "Watch: error";
        }
    }

    private void StartWatchInternal()
    {
        EnsureWatchService();
        _settings.WatchFolderPath = WatchFolderPath;
        _settings.WatchFolderEnabled = true;
        SettingsService.Save(_settings);
        _watchService!.Start(WatchFolderPath);
        IsWatchRunning = true;
        WatchStatus = $"Watch: running — {WatchFolderPath}";
    }

    [RelayCommand]
    private void StopWatch()
    {
        _watchService?.Stop();
        _settings.WatchFolderEnabled = false;
        SettingsService.Save(_settings);
        IsWatchRunning = false;
        WatchStatus = "Watch: stopped";
    }

    private void EnsureWatchService()
    {
        if (_watchService != null) return;
        _watchService = new WatchFolderService(
            AppendLog,
            () => _settings,
            () => CurrentFormat);
        _watchService.StateChanged += () =>
        {
            IsWatchRunning = _watchService?.IsRunning ?? false;
            WatchStatus = IsWatchRunning
                ? $"Watch: running — {_watchService?.WatchedPath}"
                : "Watch: stopped";
        };
    }

    [RelayCommand]
    private async Task OpenDuplicateFinderAsync()
    {
        if (HostWindow == null) return;
        var vm = new DuplicateFinderViewModel(_settings, AppendLog);
        var win = new DuplicateFinderWindow(vm);
        await win.ShowDialog(HostWindow);
    }

    [RelayCommand]
    private async Task OpenBatchCoverFillAsync()
    {
        if (HostWindow == null) return;
        var initial = !string.IsNullOrWhiteSpace(BatchFolder) && Directory.Exists(BatchFolder)
            ? BatchFolder
            : null;
        var vm = new BatchCoverFillViewModel(AppendLog, initial);
        var win = new BatchCoverFillWindow(vm);
        await win.ShowDialog(HostWindow);
    }

    [RelayCommand]
    private async Task OpenCdRipperAsync()
    {
        if (HostWindow == null) return;
        var vm = new CdRipperViewModel(_settings, AppendLog, CurrentFormat, GetMbClient);
        var win = new CdRipperWindow(vm);
        await win.ShowDialog(HostWindow);
        // CD ripper may update LastOutputFolder
        if (!string.IsNullOrWhiteSpace(_settings.LastOutputFolder)
            && !_settings.UseSourceFolderAsOutput)
            OutputFolder = _settings.LastOutputFolder!;
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void ClearLog()
    {
        // Always clear the log. When idle, also reset single-album / related form fields.
        LogText = string.Empty;
        if (IsBusy)
            return;

        AudioPath = string.Empty;
        CuePath = string.Empty;
        CoverPath = string.Empty;
        TrackListPath = string.Empty;
        Artist = string.Empty;
        Album = string.Empty;
        Year = string.Empty;
        Genre = string.Empty;
        OutputFolder = string.Empty;
        BatchFolder = string.Empty;
        ConvertFolder = string.Empty;
        // Leave WatchFolderPath alone (settings / watch related).

        Progress = 0;
        StatusText = "Ready";
        LookupStatus = string.Empty;

        _selectedReleaseId = null;
        _lookupTracks = null;

        OnPropertyChanged(nameof(CanLookupMetadata));
        OnPropertyChanged(nameof(CanDownloadCover));
        OnPropertyChanged(nameof(CanIdentifyWithAcoustId));
        OnPropertyChanged(nameof(CanLookupDiscogs));
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (HostWindow == null) return;
        var vm = new SettingsViewModel(_settings, s =>
        {
            var wasWatching = _watchService?.IsRunning == true;
            _settings = s;
            // Preserve watch enabled from live toggle; settings dialog keeps prior enabled flag
            SettingsService.Save(_settings);
            RefreshFfmpegStatus();
            if (!string.IsNullOrWhiteSpace(_settings.WatchFolderPath))
                WatchFolderPath = _settings.WatchFolderPath!;
            if (_settings.UseSourceFolderAsOutput && !string.IsNullOrWhiteSpace(AudioPath))
                SyncOutputFolderFromAudio(AudioPath);
            else if (!_settings.UseSourceFolderAsOutput && !string.IsNullOrWhiteSpace(_settings.LastOutputFolder))
                OutputFolder = _settings.LastOutputFolder!;
            AppendLog("Settings saved.");
            if (wasWatching && !string.IsNullOrWhiteSpace(WatchFolderPath))
            {
                try
                {
                    _watchService?.Stop();
                    StartWatchInternal();
                    AppendLog("Watch restarted with updated settings.");
                }
                catch (Exception ex)
                {
                    AppendLog($"Watch restart failed: {ex.Message}");
                }
            }
        });
        var win = new SettingsWindow { DataContext = vm };
        await win.ShowDialog(HostWindow);
    }

    private async Task<string?> PickFileAsync(string title, FilePickerFileType type)
    {
        if (HostWindow == null) return null;
        var files = await HostWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { type, FilePickerFileTypes.All }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText += line + Environment.NewLine;
    }

    public void ReloadSettings()
    {
        _settings = SettingsService.Load();
        RefreshFfmpegStatus();
    }

    public void Shutdown()
    {
        _watchService?.Dispose();
        _watchService = null;
        _mbClient?.Dispose();
        _mbClient = null;
        _cts?.Cancel();
    }
}
