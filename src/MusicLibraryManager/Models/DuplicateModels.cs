namespace MusicLibraryManager.Models;

/// <summary>Scanned audio file with tags and fingerprint fields for duplicate detection.</summary>
public sealed class TrackScanInfo
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Title { get; set; }
    public string? NormalizedTagKey { get; set; }
    public string? NormalizedFileName { get; set; }
}

public sealed class DuplicateGroup
{
    public string Reason { get; set; } = string.Empty;
    public List<TrackScanInfo> Tracks { get; set; } = new();
    public string DisplayLabel =>
        $"{Reason} ({Tracks.Count} files)" +
        (Tracks.Count > 0 && !string.IsNullOrWhiteSpace(Tracks[0].Title)
            ? $" — {Tracks[0].Artist} / {Tracks[0].Album} / {Tracks[0].Title}"
            : "");
}
