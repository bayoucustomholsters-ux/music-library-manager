namespace MusicLibraryManager.Models;

public sealed class AppSettings
{
    public OutputFormat DefaultFormat { get; set; } = OutputFormat.Mp3;
    public BitrateMode BitrateMode { get; set; } = BitrateMode.Cbr;
    public int Mp3BitrateKbps { get; set; } = 320;
    public int VbrQuality { get; set; } = 0; // 0 = best for LAME
    public string FilenamePattern { get; set; } = "{track:00} - {title}";
    public string FolderPattern { get; set; } = "{artist}\\{album} ({year})";
    public bool DeleteSourceAfterSuccess { get; set; } = false;
    public bool SkipAlreadyProcessed { get; set; } = true;
    public string? LastOutputFolder { get; set; }
    public string? FfmpegPath { get; set; }
    public string DefaultGenre { get; set; } = string.Empty;

    /// <summary>
    /// When true, default output folder is the directory containing the album audio file
    /// (per-album for batch). When false, use LastOutputFolder / manual picker.
    /// </summary>
    public bool UseSourceFolderAsOutput { get; set; } = true;

    /// <summary>
    /// When true, automatically search MusicBrainz after loading an audio file (if artist/album known).
    /// </summary>
    public bool AutoLookupOnLoad { get; set; } = false;

    /// <summary>
    /// When true (default), embedded tags and cover art from the source audio are
    /// carried to converted/split outputs; job/CUE/MusicBrainz overrides win when set.
    /// </summary>
    public bool PreserveSourceTags { get; set; } = true;

    // --- Phase 3: Watch Folder ---

    /// <summary>When true, watch folder monitoring should start with the app (if path is set).</summary>
    public bool WatchFolderEnabled { get; set; } = false;

    /// <summary>Folder to watch for new album audio / CUE (plus one level of subfolders).</summary>
    public string? WatchFolderPath { get; set; }

    /// <summary>Seconds to wait after a new file's size stops changing before auto-processing.</summary>
    public int WatchSettleDelaySeconds { get; set; } = 7;

    /// <summary>Duration tolerance (seconds) when matching duplicate tracks by length.</summary>
    public double DuplicateDurationToleranceSeconds { get; set; } = 2.0;

    /// <summary>Relative size tolerance (0.02 = 2%) when matching duplicates by file size.</summary>
    public double DuplicateSizeTolerancePercent { get; set; } = 0.02;

    /// <summary>Also group by normalized filename similarity when tags are missing.</summary>
    public bool DuplicateUseFilenameSimilarity { get; set; } = true;

    // --- Phase 4: AcoustID / Discogs / ReplayGain ---
    // --- Phase 5: CD Ripper / ALAC ---

    /// <summary>Free AcoustID API key from https://acoustid.org/api (never hardcode).</summary>
    public string? AcoustIdApiKey { get; set; }

    /// <summary>Optional override path to fpcalc.exe.</summary>
    public string? FpcalcPath { get; set; }

    /// <summary>Discogs personal access token (optional).</summary>
    public string? DiscogsToken { get; set; }

    /// <summary>Discogs User-Agent (required by Discogs when calling the API).</summary>
    public string DiscogsUserAgent { get; set; } = "MusicLibraryManager/5.1.0 +https://github.com/bayoucustomholsters/music-library-manager";

    /// <summary>When true, compute and write ReplayGain tags on convert/split (default off).</summary>
    public bool ApplyReplayGain { get; set; } = false;
}
