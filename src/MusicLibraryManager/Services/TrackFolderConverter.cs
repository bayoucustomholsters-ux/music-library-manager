using MusicLibraryManager.Models;
using File = System.IO.File;

namespace MusicLibraryManager.Services;

/// <summary>
/// Converts a folder of already-split tracks (each file = one job), preserving tags.
/// Unlike AlbumSplitter batch discovery, does not pick one primary file per directory.
/// </summary>
public sealed class TrackFolderConverter
{
    private readonly AppSettings _settings;
    private readonly Action<string>? _log;
    private readonly Action<double>? _progress;

    public TrackFolderConverter(AppSettings settings, Action<string>? log = null, Action<double>? progress = null)
    {
        _settings = settings;
        _log = log;
        _progress = progress;
    }

    public async Task ConvertFolderAsync(
        string sourceFolder,
        string? outputRoot,
        OutputFormat format,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException(sourceFolder);

        var ffmpegPath = FfmpegLocator.FindFfmpeg(_settings.FfmpegPath)
            ?? throw new InvalidOperationException(
                "ffmpeg.exe not found. Place ffmpeg.exe next to the app or in an 'ffmpeg' subfolder, or set the path in Settings.");

        var ffmpeg = new FfmpegService(ffmpegPath);
        var files = Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => AlbumSplitter.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Log($"Convert folder: {files.Count} track(s) under {sourceFolder}");
        if (files.Count == 0) return;

        var useSource = _settings.UseSourceFolderAsOutput;
        var outExt = format switch
        {
            OutputFormat.Mp3 => ".mp3",
            OutputFormat.Flac => ".flac",
            OutputFormat.Alac => ".m4a",
            OutputFormat.KeepOriginal => null,
            _ => ".mp3"
        };

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var src = files[i];
            try
            {
                await ConvertOneAsync(ffmpeg, ffmpegPath, src, sourceFolder, outputRoot, useSource, format, outExt, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log($"ERROR converting {Path.GetFileName(src)}: {ex.Message}");
            }

            _progress?.Invoke((i + 1) / (double)files.Count);
        }

        Log("Convert folder finished.");
    }

    private async Task ConvertOneAsync(
        FfmpegService ffmpeg,
        string ffmpegPath,
        string src,
        string sourceFolder,
        string? outputRoot,
        bool useSource,
        OutputFormat format,
        string? outExt,
        CancellationToken ct)
    {
        var srcExt = Path.GetExtension(src).ToLowerInvariant();
        var destExt = outExt ?? srcExt;

        string destDir;
        if (useSource)
        {
            destDir = Path.GetDirectoryName(src)!;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
                throw new InvalidOperationException("Output folder required when same-as-source is disabled.");
            var relDir = Path.GetDirectoryName(Path.GetRelativePath(sourceFolder, src)) ?? "";
            destDir = string.IsNullOrEmpty(relDir)
                ? outputRoot!
                : Path.Combine(outputRoot!, relDir);
            Directory.CreateDirectory(destDir);
        }

        var destName = Path.GetFileNameWithoutExtension(src) + destExt;
        var dest = Path.Combine(destDir, destName);

        // Same path + same format: nothing to do
        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)
            && format == OutputFormat.KeepOriginal)
        {
            Log($"Skip (already original): {Path.GetFileName(src)}");
            return;
        }

        // Avoid overwriting source when converting in place to a different name is fine;
        // if same extension and same path, write via temp then replace.
        var samePath = string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase);
        var writePath = samePath
            ? Path.Combine(destDir, Path.GetFileNameWithoutExtension(src) + ".mlm-tmp" + destExt)
            : dest;

        Log($"  {Path.GetFileName(src)} → {Path.GetFileName(dest)}");

        if (format == OutputFormat.KeepOriginal && !samePath)
        {
            File.Copy(src, writePath, overwrite: true);
        }
        else
        {
            await ffmpeg.ConvertWholeFileAsync(src, writePath, format == OutputFormat.KeepOriginal ? OutputFormat.KeepOriginal : format, _settings, ct)
                .ConfigureAwait(false);
        }

        try
        {
            AudioTagInfo? sourceTags = null;
            if (_settings.PreserveSourceTags)
            {
                try { sourceTags = TaggingService.ReadTags(src); }
                catch { /* ignore */ }
            }

            var merged = TaggingService.MergeTags(
                sourceTags,
                preserveSource: _settings.PreserveSourceTags);
            // Keep title from filename if tags empty
            if (string.IsNullOrWhiteSpace(merged.Title))
                merged.Title = Path.GetFileNameWithoutExtension(src);

            TaggingService.ApplyTags(writePath, merged, coverPath: null);
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
                await rg.ApplyToFileAsync(writePath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"  Warning: ReplayGain failed: {ex.Message}");
            }
        }

        if (samePath)
        {
            File.Delete(src);
            File.Move(writePath, dest);
        }
    }

    private void Log(string msg) => _log?.Invoke(msg);
}
