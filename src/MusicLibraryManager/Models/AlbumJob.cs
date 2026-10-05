namespace MusicLibraryManager.Models;

public sealed class AlbumJob
{
    public string AudioPath { get; set; } = string.Empty;
    public string? CuePath { get; set; }
    public string? CoverPath { get; set; }
    public string? TrackListPath { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// In-memory track list from MusicBrainz (or similar) for equal-duration split without a CUE.
    /// </summary>
    public List<MusicBrainzTrackInfo>? LookupTracks { get; set; }

    public string? MusicBrainzReleaseId { get; set; }
}
