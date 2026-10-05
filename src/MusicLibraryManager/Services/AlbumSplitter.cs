using MusicLibraryManager.Models;
using File = System.IO.File;

namespace MusicLibraryManager.Services;

public sealed class AlbumSplitter
{
    public static readonly string[] AudioExtensions =
    {
        ".mp3", ".flac", ".wav", ".ape", ".m4a", ".alac", ".wma", ".aiff", ".aif", ".ogg", ".oga"
    };

    public static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png" };

    private readonly AppSettings _settings;
    private readonly Action<string>? _log;
    private readonly Action<double>? _progress;

    public AlbumSplitter(AppSettings settings, Action<string>? log = null, Action<double>? progress = null)
    {
        _settings = settings;
        _log = log;
        _progress = progress;
    }

    public async Task ProcessAlbumAsync(AlbumJob job, string outputRoot, OutputFormat format, CancellationToken ct = default)
    {
        var ffmpegPath = FfmpegLocator.FindFfmpeg(_settings.FfmpegPath)
            ?? throw new InvalidOperationException(
                "ffmpeg.exe not found. Place ffmpeg.exe next to the app or in an 'ffmpeg' subfolder, or set the path in Settings.");

        var ffmpeg = new FfmpegService(ffmpegPath);
        Log($"Processing: {job.AudioPath}");

        CueSheet? cue = null;
        if (!string.IsNullOrWhiteSpace(job.CuePath) && File.Exists(job.CuePath))
        {
            cue = CueParser.ParseFile(job.CuePath);
            Log($"Loaded CUE: {Path.GetFileName(job.CuePath)} ({cue.Tracks.Count} tracks)");
        }

        AudioTagInfo? sourceTags = null;
        if (_settings.PreserveSourceTags && !string.IsNullOrWhiteSpace(job.AudioPath) && File.Exists(job.AudioPath))
        {
            try
            {
                sourceTags = TaggingService.ReadTags(job.AudioPath);
                Log("Read embedded tags from source file.");
            }
            catch (Exception ex)
            {
                Log($"Warning: could not read source tags: {ex.Message}");
            }
        }

        // Precedence: job (UI/MB) > CUE > source tags > defaults
        var artist = TaggingService.FirstNonEmpty(job.Artist, cue?.Performer, sourceTags?.Artist, "Unknown Artist");
        var album = TaggingService.FirstNonEmpty(job.Album, cue?.Title, sourceTags?.Album, Path.GetFileNameWithoutExtension(job.AudioPath));
        var year = TaggingService.FirstNonEmpty(job.Year, cue?.Date, sourceTags?.Year, "");
        var genre = TaggingService.FirstNonEmpty(job.Genre, cue?.Genre, sourceTags?.Genre, _settings.DefaultGenre);
        year = TaggingService.NormalizeYear(year) ?? "";

        var folderValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["artist"] = FilenameSanitizer.Sanitize(artist),
            ["album"] = FilenameSanitizer.Sanitize(album),
            ["year"] = string.IsNullOrEmpty(year) ? "0000" : FilenameSanitizer.Sanitize(year)
        };

        var relativeFolder = FilenameSanitizer.ApplyPattern(_settings.FolderPattern, folderValues);
        // Normalize separators
        relativeFolder = relativeFolder.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var albumDir = Path.Combine(outputRoot, relativeFolder);

        if (_settings.SkipAlreadyProcessed && Directory.Exists(albumDir))
        {
            var existing = Directory.GetFiles(albumDir, "*.*", SearchOption.TopDirectoryOnly);
            var expectedCount = cue?.Tracks.Count
                ?? job.LookupTracks?.Count
                ?? EstimateTrackListCount(job.TrackListPath)
                ?? 1;
            if (existing.Length >= expectedCount)
            {
                Log($"Skip (already processed): {albumDir}");
                job.Status = "Skipped";
                return;
            }
        }

        Directory.CreateDirectory(albumDir);

        // Copy cover into album folder — separate cover file wins over embedded art
        string? embeddedCover = null;
        if (!string.IsNullOrWhiteSpace(job.CoverPath) && File.Exists(job.CoverPath))
        {
            var destCover = Path.Combine(albumDir, "cover" + Path.GetExtension(job.CoverPath).ToLowerInvariant());
            File.Copy(job.CoverPath, destCover, overwrite: true);
            embeddedCover = destCover;
            Log($"Cover copied: {Path.GetFileName(destCover)}");
        }

        List<(int num, string title, string performer, TimeSpan start, TimeSpan? end)> tracks;
        var equalSplit = false;

        if (cue != null && cue.Tracks.Count > 0)
        {
            tracks = cue.Tracks.Select(t => (
                t.Number,
                string.IsNullOrWhiteSpace(t.Title) ? $"Track {t.Number:00}" : t.Title,
                string.IsNullOrWhiteSpace(t.Performer) ? artist : t.Performer,
                t.StartTime,
                t.EndTime
            )).ToList();
        }
        else
        {
            var titles = ResolveTrackTitles(job);
            if (titles.Count > 1)
            {
                tracks = BuildEqualDurationTracks(job.AudioPath, titles, artist);
                equalSplit = true;
                Log($"Equal-duration split: {titles.Count} tracks (no CUE; titles from MusicBrainz / track list).");
            }
            else if (titles.Count == 1)
            {
                tracks = new List<(int, string, string, TimeSpan, TimeSpan?)>
                {
                    (1, titles[0].Title, artist, TimeSpan.Zero, null)
                };
                Log("Single track title — converting entire file.");
            }
            else
            {
                // Whole-file convert: preserve source title when available
                var singleTitle = TaggingService.FirstNonEmpty(sourceTags?.Title, album);
                tracks = new List<(int, string, string, TimeSpan, TimeSpan?)>
                {
                    (1, singleTitle, artist, TimeSpan.Zero, null)
                };
                Log("No CUE / track list — converting entire file as one track.");
            }
        }

        var ext = format switch
        {
            OutputFormat.Mp3 => ".mp3",
            OutputFormat.Flac => ".flac",
            OutputFormat.Alac => ".m4a",
            OutputFormat.KeepOriginal => Path.GetExtension(job.AudioPath).ToLowerInvariant(),
            _ => ".mp3"
        };

        var trackCount = tracks.Count;
        var isMultiTrackSplit = (cue != null && cue.Tracks.Count > 0) || equalSplit;

        for (var i = 0; i < tracks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (num, title, performer, start, end) = tracks[i];

            var fileValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["track"] = num.ToString(),
                ["title"] = FilenameSanitizer.Sanitize(title),
                ["artist"] = FilenameSanitizer.Sanitize(performer),
                ["album"] = FilenameSanitizer.Sanitize(album),
                ["year"] = year
            };

            var baseName = FilenameSanitizer.ApplyPattern(_settings.FilenamePattern, fileValues);
            baseName = FilenameSanitizer.Sanitize(baseName);
            var outPath = Path.Combine(albumDir, baseName + ext);

            Log($"  [{num}/{trackCount}] {baseName}{ext}");

            if (isMultiTrackSplit)
            {
                await ffmpeg.ExtractAndConvertAsync(job.AudioPath, outPath, start, end, format, _settings, ct)
                    .ConfigureAwait(false);
            }
            else if (format == OutputFormat.KeepOriginal)
            {
                File.Copy(job.AudioPath, outPath, overwrite: true);
            }
            else
            {
                await ffmpeg.ConvertWholeFileAsync(job.AudioPath, outPath, format, _settings, ct)
                    .ConfigureAwait(false);
            }

            try
            {
                // Album-level from source+job; per-track title/performer/track# from CUE/split win
                var merged = TaggingService.MergeTags(
                    sourceTags,
                    overrideTitle: title,
                    overrideArtist: performer,
                    overrideAlbumArtist: artist,
                    overrideAlbum: album,
                    overrideYear: year,
                    overrideGenre: genre,
                    overrideTrack: num,
                    overrideTrackCount: isMultiTrackSplit
                        ? trackCount
                        : (int?)(sourceTags?.TrackCount is > 0 ? (int)sourceTags.TrackCount.Value : trackCount),
                    preserveSource: _settings.PreserveSourceTags);

                // Prefer separate cover file; else keep source embedded pictures from merge
                TaggingService.ApplyTags(outPath, merged, embeddedCover);
            }
            catch (Exception ex)
            {
                Log($"  Warning: tagging failed: {ex.Message}");
            }

            if (_settings.ApplyReplayGain)
            {
                try
                {
                    var rg = new ReplayGainService(ffmpegPath, Log);
                    await rg.ApplyToFileAsync(outPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"  Warning: ReplayGain failed: {ex.Message}");
                }
            }

            _progress?.Invoke((i + 1) / (double)trackCount);
        }

        if (_settings.DeleteSourceAfterSuccess)
        {
            try
            {
                File.Delete(job.AudioPath);
                if (!string.IsNullOrWhiteSpace(job.CuePath) && File.Exists(job.CuePath))
                    File.Delete(job.CuePath);
                Log("Source deleted per settings.");
            }
            catch (Exception ex)
            {
                Log($"Could not delete source: {ex.Message}");
            }
        }

        job.Status = "Done";
        Log($"Finished: {albumDir}");
    }

    /// <summary>
    /// Builds equal-duration track segments from titles. Pure helper for tests.
    /// </summary>
    public static List<(int num, string title, string performer, TimeSpan start, TimeSpan? end)> BuildEqualDurationSegments(
        TimeSpan totalDuration,
        IReadOnlyList<MusicBrainzTrackInfo> titles,
        string artist)
    {
        if (titles.Count == 0)
            return new List<(int, string, string, TimeSpan, TimeSpan?)>();

        if (totalDuration <= TimeSpan.Zero)
        {
            return titles.Select((t, i) => (
                t.Number > 0 ? t.Number : i + 1,
                t.Title,
                artist,
                TimeSpan.Zero,
                (TimeSpan?)null
            )).Take(1).ToList();
        }

        var n = titles.Count;
        var sliceTicks = totalDuration.Ticks / n;
        var list = new List<(int, string, string, TimeSpan, TimeSpan?)>(n);
        for (var i = 0; i < n; i++)
        {
            var start = TimeSpan.FromTicks(sliceTicks * i);
            TimeSpan? end = i < n - 1
                ? TimeSpan.FromTicks(sliceTicks * (i + 1))
                : null; // last track to EOF
            var num = titles[i].Number > 0 ? titles[i].Number : i + 1;
            list.Add((num, titles[i].Title, artist, start, end));
        }
        return list;
    }

    private List<(int num, string title, string performer, TimeSpan start, TimeSpan? end)> BuildEqualDurationTracks(
        string audioPath,
        IReadOnlyList<MusicBrainzTrackInfo> titles,
        string artist)
    {
        var duration = GetAudioDuration(audioPath);
        if (duration <= TimeSpan.Zero)
        {
            Log("Warning: could not read audio duration — converting as single file.");
            return new List<(int, string, string, TimeSpan, TimeSpan?)>
            {
                (1, titles[0].Title, artist, TimeSpan.Zero, null)
            };
        }

        Log($"Audio duration: {duration:hh\\:mm\\:ss}");
        return BuildEqualDurationSegments(duration, titles, artist);
    }

    private static TimeSpan GetAudioDuration(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            return file.Properties.Duration;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static List<MusicBrainzTrackInfo> ResolveTrackTitles(AlbumJob job)
    {
        if (job.LookupTracks is { Count: > 0 })
            return job.LookupTracks;

        if (!string.IsNullOrWhiteSpace(job.TrackListPath) && File.Exists(job.TrackListPath))
        {
            var parsed = TrackListParser.Parse(job.TrackListPath);
            return parsed.Select((title, i) => new MusicBrainzTrackInfo
            {
                Number = i + 1,
                Title = title
            }).ToList();
        }

        return new List<MusicBrainzTrackInfo>();
    }

    private static int? EstimateTrackListCount(string? trackListPath)
    {
        if (string.IsNullOrWhiteSpace(trackListPath) || !File.Exists(trackListPath))
            return null;
        try
        {
            var n = TrackListParser.Parse(trackListPath).Count;
            return n > 0 ? n : null;
        }
        catch
        {
            return null;
        }
    }

    public static AlbumJob? DiscoverFromAudio(string audioPath)
    {
        if (!File.Exists(audioPath)) return null;
        var dir = Path.GetDirectoryName(audioPath)!;
        var stem = Path.GetFileNameWithoutExtension(audioPath);

        var cue = Directory.GetFiles(dir, "*.cue")
            .FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), stem, StringComparison.OrdinalIgnoreCase))
            ?? Directory.GetFiles(dir, "*.cue").FirstOrDefault();

        var cover = CoverExtensions
            .SelectMany(ext => Directory.GetFiles(dir, "*" + ext))
            .OrderBy(f =>
            {
                var n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (n is "cover" or "folder" or "front" or "album") return 0;
                return 1;
            })
            .FirstOrDefault();

        var trackList = new[] { "*.txt", "*tracklist*", "*tracks*" }
            .SelectMany(p => Directory.GetFiles(dir, p))
            .FirstOrDefault(f => !f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));

        string? artist = null, album = null, year = null, genre = null;
        if (cue != null)
        {
            try
            {
                var sheet = CueParser.ParseFile(cue);
                artist = sheet.Performer;
                album = sheet.Title;
                year = sheet.Date;
                genre = sheet.Genre;
            }
            catch { /* ignore */ }
        }

        // Fill gaps from embedded source tags when CUE (or nothing) didn't provide them
        try
        {
            var source = TaggingService.ReadTags(audioPath);
            artist = TaggingService.FirstNonEmpty(artist, source.Artist, source.AlbumArtist);
            album = TaggingService.FirstNonEmpty(album, source.Album);
            year = TaggingService.FirstNonEmpty(year, source.Year);
            genre = TaggingService.FirstNonEmpty(genre, source.Genre);
            if (string.IsNullOrWhiteSpace(artist)) artist = null;
            if (string.IsNullOrWhiteSpace(album)) album = null;
            if (string.IsNullOrWhiteSpace(year)) year = null;
            if (string.IsNullOrWhiteSpace(genre)) genre = null;
        }
        catch { /* ignore unreadable tags */ }

        return new AlbumJob
        {
            AudioPath = audioPath,
            CuePath = cue,
            CoverPath = cover,
            TrackListPath = trackList,
            Artist = artist,
            Album = album,
            Year = year,
            Genre = genre
        };
    }

    public static List<AlbumJob> DiscoverBatch(string folder)
    {
        var jobs = new List<AlbumJob>();
        if (!Directory.Exists(folder)) return jobs;

        // Prefer nested album folders; also flat files in root
        var audioFiles = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(IsLikelyAlbumFile) // large album files, skip short clips heuristically by name
            .ToList();

        // Group by directory — one primary audio per dir (largest file)
        foreach (var group in audioFiles.GroupBy(f => Path.GetDirectoryName(f)!, StringComparer.OrdinalIgnoreCase))
        {
            var primary = group
                .OrderByDescending(f => new FileInfo(f).Length)
                .First();
            var job = DiscoverFromAudio(primary);
            if (job != null)
                jobs.Add(job);
        }

        return jobs;
    }

    /// <summary>
    /// True when the filename looks like a full album image (not an already-split track like "01 - Song").
    /// </summary>
    public static bool IsLikelyAlbumFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        // Skip obvious track-already-split files like "01 - song"
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{1,3}\s*[-_.]"))
            return false;
        return true;
    }

    private void Log(string msg) => _log?.Invoke(msg);
}
