using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicLibraryManager.Models;

public sealed class CdDriveInfo
{
    public string Letter { get; init; } = "";
    public string RootPath { get; init; } = "";
    public string DevicePath { get; init; } = "";
    public bool IsReady { get; init; }
    public string? VolumeLabel { get; init; }

    public string DisplayName
    {
        get
        {
            var letter = string.IsNullOrWhiteSpace(Letter) ? DevicePath : Letter;
            if (!IsReady)
                return $"{letter} — no disc";
            return string.IsNullOrWhiteSpace(VolumeLabel)
                ? $"{letter} — disc inserted"
                : $"{letter} — {VolumeLabel}";
        }
    }
}

public sealed class CdToc
{
    public int FirstTrack { get; init; }
    public int LastTrack { get; init; }
    /// <summary>Lead-out offset in CD frames (LBA + 150), used for DiscID.</summary>
    public int LeadOutOffset { get; init; }
    public IReadOnlyList<CdTrack> Tracks { get; init; } = Array.Empty<CdTrack>();
    public string? DriveLetter { get; init; }
    public string? DevicePath { get; init; }

    public IEnumerable<CdTrack> AudioTracks => Tracks.Where(t => t.IsAudio);

    public int AudioTrackCount => Tracks.Count(t => t.IsAudio);

    public TimeSpan TotalAudioLength =>
        TimeSpan.FromSeconds(AudioTracks.Sum(t => t.LengthSectors) / 75.0);
}

public sealed class CdTrack
{
    public int Number { get; init; }
    /// <summary>Start offset in CD frames (LBA + 150) for DiscID.</summary>
    public int OffsetFrames { get; init; }
    public int StartLba { get; init; }
    public int LengthSectors { get; init; }
    public bool IsAudio { get; init; }

    public TimeSpan Length => TimeSpan.FromSeconds(LengthSectors / 75.0);
}

public partial class CdRipTrackItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _status = "";

    public int Number { get; init; }
    public TimeSpan Length { get; init; }
    public int StartLba { get; init; }
    public int LengthSectors { get; init; }
    public bool IsAudio { get; init; } = true;

    public string LengthText
    {
        get
        {
            var t = Length;
            return t.TotalHours >= 1
                ? t.ToString(@"h\:mm\:ss")
                : t.ToString(@"m\:ss");
        }
    }

    public string DisplayLabel => $"{Number:00}. {Title}  [{LengthText}]";

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayLabel));
}
