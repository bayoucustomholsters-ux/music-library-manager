namespace MusicLibraryManager.Models;

/// <summary>
/// Snapshot of embedded audio tags (and optional cover pictures) read from a source file
/// or produced by merging source tags with job / CUE / MusicBrainz overrides.
/// </summary>
public sealed class AudioTagInfo
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Album { get; set; }
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public uint? Track { get; set; }
    public uint? TrackCount { get; set; }
    public uint? Disc { get; set; }
    public uint? DiscCount { get; set; }

    /// <summary>
    /// Copied picture payloads from the source file (safe after source TagLib.File is disposed).
    /// </summary>
    public IReadOnlyList<EmbeddedPicture>? Pictures { get; set; }
}

/// <summary>
/// Independent copy of embedded artwork bytes (avoids TagLib lifetime issues).
/// </summary>
public sealed class EmbeddedPicture
{
    public string? MimeType { get; set; }
    public string? Description { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    /// <summary>TagLib PictureType as int (e.g. 3 = FrontCover).</summary>
    public int Type { get; set; } = 3;
}
