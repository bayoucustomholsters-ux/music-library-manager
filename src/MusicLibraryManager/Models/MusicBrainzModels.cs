namespace MusicLibraryManager.Models;

public sealed class MusicBrainzReleaseCandidate
{
    public string ReleaseId { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public int TrackCount { get; set; }
    public string? Disambiguation { get; set; }
    public string? Country { get; set; }
    public string? Status { get; set; }
    /// <summary>MusicBrainz search score (0–100) when present.</summary>
    public int? Score { get; set; }
    public List<MusicBrainzTrackInfo> Tracks { get; set; } = new();

    public string DisplayLabel
    {
        get
        {
            var year = string.IsNullOrWhiteSpace(Year) ? "????" : Year;
            var extra = string.IsNullOrWhiteSpace(Disambiguation) ? "" : $" — {Disambiguation}";
            var country = string.IsNullOrWhiteSpace(Country) ? "" : $" [{Country}]";
            return $"{Artist} — {Album} ({year}){country} · {TrackCount} tracks{extra}";
        }
    }
}

public sealed class MusicBrainzTrackInfo
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? LengthMs { get; set; }
}
