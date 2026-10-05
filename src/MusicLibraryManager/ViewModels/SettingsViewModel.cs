using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly Action<AppSettings> _onSave;
    private readonly string? _lastOutputFolder;
    private readonly bool _watchFolderEnabled;
    private readonly string? _watchFolderPath;

    [ObservableProperty] private int _formatIndex;
    [ObservableProperty] private int _bitrateModeIndex;
    [ObservableProperty] private double _mp3Bitrate = 320;
    [ObservableProperty] private double _vbrQuality;
    [ObservableProperty] private string _filenamePattern = "{track:00} - {title}";
    [ObservableProperty] private string _folderPattern = "{artist}\\{album} ({year})";
    [ObservableProperty] private bool _deleteSource;
    [ObservableProperty] private bool _skipProcessed = true;
    [ObservableProperty] private string _ffmpegPath = string.Empty;
    [ObservableProperty] private string _defaultGenre = string.Empty;
    [ObservableProperty] private bool _useSourceFolderAsOutput = true;
    [ObservableProperty] private bool _autoLookupOnLoad;
    [ObservableProperty] private bool _preserveSourceTags = true;
    [ObservableProperty] private string _watchFolderPathEdit = string.Empty;
    [ObservableProperty] private double _watchSettleDelaySeconds = 7;
    [ObservableProperty] private bool _duplicateUseFilenameSimilarity = true;
    [ObservableProperty] private double _duplicateDurationToleranceSeconds = 2;
    [ObservableProperty] private double _duplicateSizeTolerancePercent = 2;

    // Phase 4
    [ObservableProperty] private string _acoustIdApiKey = string.Empty;
    [ObservableProperty] private string _fpcalcPath = string.Empty;
    [ObservableProperty] private string _discogsToken = string.Empty;
    [ObservableProperty] private string _discogsUserAgent = DiscogsClient.DefaultUserAgent;
    [ObservableProperty] private bool _applyReplayGain;

    public SettingsViewModel(AppSettings settings, Action<AppSettings> onSave)
    {
        _onSave = onSave;
        _lastOutputFolder = settings.LastOutputFolder;
        _watchFolderEnabled = settings.WatchFolderEnabled;
        _watchFolderPath = settings.WatchFolderPath;
        FormatIndex = settings.DefaultFormat switch
        {
            OutputFormat.Flac => 1,
            OutputFormat.Alac => 2,
            OutputFormat.KeepOriginal => 3,
            _ => 0
        };
        BitrateModeIndex = settings.BitrateMode == BitrateMode.Vbr ? 1 : 0;
        Mp3Bitrate = settings.Mp3BitrateKbps;
        VbrQuality = settings.VbrQuality;
        FilenamePattern = settings.FilenamePattern;
        FolderPattern = settings.FolderPattern;
        DeleteSource = settings.DeleteSourceAfterSuccess;
        SkipProcessed = settings.SkipAlreadyProcessed;
        FfmpegPath = settings.FfmpegPath ?? "";
        DefaultGenre = settings.DefaultGenre;
        UseSourceFolderAsOutput = settings.UseSourceFolderAsOutput;
        AutoLookupOnLoad = settings.AutoLookupOnLoad;
        PreserveSourceTags = settings.PreserveSourceTags;
        WatchFolderPathEdit = settings.WatchFolderPath ?? "";
        WatchSettleDelaySeconds = settings.WatchSettleDelaySeconds;
        DuplicateUseFilenameSimilarity = settings.DuplicateUseFilenameSimilarity;
        DuplicateDurationToleranceSeconds = settings.DuplicateDurationToleranceSeconds;
        DuplicateSizeTolerancePercent = settings.DuplicateSizeTolerancePercent * 100.0;

        AcoustIdApiKey = settings.AcoustIdApiKey ?? "";
        FpcalcPath = settings.FpcalcPath ?? "";
        DiscogsToken = settings.DiscogsToken ?? "";
        DiscogsUserAgent = string.IsNullOrWhiteSpace(settings.DiscogsUserAgent)
            ? DiscogsClient.DefaultUserAgent
            : settings.DiscogsUserAgent;
        ApplyReplayGain = settings.ApplyReplayGain;
    }

    [RelayCommand]
    private void Save()
    {
        var s = new AppSettings
        {
            DefaultFormat = FormatIndex switch
            {
                1 => OutputFormat.Flac,
                2 => OutputFormat.Alac,
                3 => OutputFormat.KeepOriginal,
                _ => OutputFormat.Mp3
            },
            BitrateMode = BitrateModeIndex == 1 ? BitrateMode.Vbr : BitrateMode.Cbr,
            Mp3BitrateKbps = (int)Mp3Bitrate,
            VbrQuality = (int)VbrQuality,
            FilenamePattern = FilenamePattern,
            FolderPattern = FolderPattern,
            DeleteSourceAfterSuccess = DeleteSource,
            SkipAlreadyProcessed = SkipProcessed,
            FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath.Trim(),
            DefaultGenre = DefaultGenre,
            LastOutputFolder = _lastOutputFolder,
            UseSourceFolderAsOutput = UseSourceFolderAsOutput,
            AutoLookupOnLoad = AutoLookupOnLoad,
            PreserveSourceTags = PreserveSourceTags,
            WatchFolderEnabled = _watchFolderEnabled,
            WatchFolderPath = string.IsNullOrWhiteSpace(WatchFolderPathEdit)
                ? _watchFolderPath
                : WatchFolderPathEdit.Trim(),
            WatchSettleDelaySeconds = (int)Math.Clamp(WatchSettleDelaySeconds, 1, 120),
            DuplicateUseFilenameSimilarity = DuplicateUseFilenameSimilarity,
            DuplicateDurationToleranceSeconds = DuplicateDurationToleranceSeconds,
            DuplicateSizeTolerancePercent = Math.Clamp(DuplicateSizeTolerancePercent, 0, 50) / 100.0,
            AcoustIdApiKey = string.IsNullOrWhiteSpace(AcoustIdApiKey) ? null : AcoustIdApiKey.Trim(),
            FpcalcPath = string.IsNullOrWhiteSpace(FpcalcPath) ? null : FpcalcPath.Trim(),
            DiscogsToken = string.IsNullOrWhiteSpace(DiscogsToken) ? null : DiscogsToken.Trim(),
            DiscogsUserAgent = string.IsNullOrWhiteSpace(DiscogsUserAgent)
                ? DiscogsClient.DefaultUserAgent
                : DiscogsUserAgent.Trim(),
            ApplyReplayGain = ApplyReplayGain
        };
        _onSave(s);
    }
}
