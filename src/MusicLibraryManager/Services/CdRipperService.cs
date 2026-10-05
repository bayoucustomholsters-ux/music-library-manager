using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Rip selected CDDA tracks to FLAC or MP3 (WAV temp → bundled ffmpeg), then tag.
/// CDs have no source tags — metadata comes from MusicBrainz / the user.
/// </summary>
public sealed class CdRipperService
{
    private readonly AppSettings _settings;
    private readonly Action<string>? _log;
    private readonly Action<double>? _progress;

    public CdRipperService(AppSettings settings, Action<string>? log = null, Action<double>? progress = null)
    {
        _settings = settings;
        _log = log;
        _progress = progress;
    }

    public static string BuildAlbumDirectory(AppSettings settings, string outputRoot, string artist, string album, string? year)
    {
        var y = TaggingService.NormalizeYear(year) ?? "";
        var folderValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["artist"] = FilenameSanitizer.Sanitize(artist),
            ["album"] = FilenameSanitizer.Sanitize(album),
            ["year"] = string.IsNullOrEmpty(y) ? "0000" : FilenameSanitizer.Sanitize(y)
        };
        var relative = FilenameSanitizer.ApplyPattern(settings.FolderPattern, folderValues);
        relative = relative.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(outputRoot, relative);
    }

    public static string BuildTrackFileName(AppSettings settings, int trackNumber, string title, string artist, string album, string? year, OutputFormat format)
    {
        var y = TaggingService.NormalizeYear(year) ?? "";
        var fileValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["track"] = trackNumber.ToString(),
            ["title"] = FilenameSanitizer.Sanitize(title),
            ["artist"] = FilenameSanitizer.Sanitize(artist),
            ["album"] = FilenameSanitizer.Sanitize(album),
            ["year"] = y
        };
        var baseName = FilenameSanitizer.Sanitize(
            FilenameSanitizer.ApplyPattern(settings.FilenamePattern, fileValues));
        return baseName + ExtensionFor(format);
    }

    public static string ExtensionFor(OutputFormat format) => format switch
    {
        OutputFormat.Flac => ".flac",
        OutputFormat.Alac => ".m4a",
        OutputFormat.KeepOriginal => ".wav",
        _ => ".mp3"
    };

    public async Task<string> RipAsync(
        CdDriveInfo drive,
        CdToc toc,
        IReadOnlyList<CdRipTrackItem> selected,
        string outputRoot,
        OutputFormat format,
        string artist,
        string album,
        string? year,
        string? genre,
        string? coverPath,
        CancellationToken ct = default)
    {
        if (selected.Count == 0)
            throw new InvalidOperationException("Select at least one track to rip.");
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new InvalidOperationException("Choose an output folder.");

        Directory.CreateDirectory(outputRoot);

        artist = TaggingService.FirstNonEmpty(artist, "Unknown Artist");
        album = TaggingService.FirstNonEmpty(album, drive.VolumeLabel, "Unknown Album");
        year = TaggingService.NormalizeYear(year) ?? "";
        genre = TaggingService.FirstNonEmpty(genre, _settings.DefaultGenre);

        if (format == OutputFormat.KeepOriginal)
            format = OutputFormat.Flac;

        var ffmpegPath = FfmpegLocator.FindFfmpeg(_settings.FfmpegPath);
        if (ffmpegPath == null)
            throw new InvalidOperationException(
                "ffmpeg.exe not found. Place ffmpeg.exe next to the app or in an 'ffmpeg' subfolder, or set the path in Settings.");

        var ffmpeg = new FfmpegService(ffmpegPath);
        var albumDir = BuildAlbumDirectory(_settings, outputRoot, artist, album, year);
        Directory.CreateDirectory(albumDir);
        Log($"Output: {albumDir}");

        string? embeddedCover = null;
        if (!string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath))
        {
            var destCover = Path.Combine(albumDir, "cover" + Path.GetExtension(coverPath).ToLowerInvariant());
            File.Copy(coverPath, destCover, overwrite: true);
            embeddedCover = destCover;
            Log($"Cover copied: {Path.GetFileName(destCover)}");
        }

        var tracksByNumber = toc.Tracks.ToDictionary(t => t.Number);
        var total = selected.Count;
        var tempDir = Path.Combine(Path.GetTempPath(), "mlm-cdrip-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = selected[i];
                if (!tracksByNumber.TryGetValue(item.Number, out var cdTrack))
                    throw new InvalidOperationException($"Track {item.Number} is not in the TOC.");
                if (!cdTrack.IsAudio)
                {
                    Log($"Skipping data track {item.Number}.");
                    continue;
                }

                var title = string.IsNullOrWhiteSpace(item.Title) ? $"Track {item.Number:00}" : item.Title;
                var fileName = BuildTrackFileName(_settings, item.Number, title, artist, album, year, format);
                var outPath = Path.Combine(albumDir, fileName);
                var wavPath = Path.Combine(tempDir, $"track{item.Number:00}.wav");

                Log($"Ripping track {item.Number}/{toc.AudioTrackCount}: {title}");
                item.Status = "Ripping…";

                var baseProgress = i / (double)total;
                var span = 1.0 / total;
                await CdDriveService.RipTrackToWavAsync(
                    drive,
                    cdTrack,
                    wavPath,
                    p => _progress?.Invoke(baseProgress + p * span * 0.75),
                    ct).ConfigureAwait(false);

                item.Status = "Encoding…";
                Log($"  Encoding → {fileName}");
                await ffmpeg.ConvertWholeFileAsync(wavPath, outPath, format, _settings, ct)
                    .ConfigureAwait(false);

                try
                {
                    // CD has no source tags — PreserveSourceTags does not apply.
                    TaggingService.ApplyTags(
                        outPath,
                        artist,
                        album,
                        title,
                        item.Number,
                        toc.AudioTrackCount,
                        year,
                        genre,
                        embeddedCover);
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

                try { File.Delete(wavPath); }
                catch { /* temp cleanup */ }

                item.Status = "Done";
                _progress?.Invoke((i + 1) / (double)total);
                Log($"  Wrote {outPath}");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { /* ignore */ }
        }

        Log("Rip complete.");
        return albumDir;
    }

    private void Log(string msg) => _log?.Invoke(msg);
}
