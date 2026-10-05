using System.Text.RegularExpressions;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Scans a folder for likely duplicate audio tracks using normalized tags,
/// size+duration tolerance, and optional filename similarity.
/// </summary>
public static class DuplicateFinderService
{
    private static readonly Regex NonAlnum = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes artist/album/title into a stable comparison key.
    /// Pure helper — covered by unit tests.
    /// </summary>
    public static string NormalizeTagKey(string? artist, string? album, string? title)
    {
        var a = NormalizeToken(artist);
        var b = NormalizeToken(album);
        var c = NormalizeToken(title);
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) && string.IsNullOrEmpty(c))
            return string.Empty;
        return $"{a}|{b}|{c}";
    }

    /// <summary>Normalize a single tag or filename token for comparison.</summary>
    public static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim().ToLowerInvariant();
        // Strip common featuring markers noise lightly
        s = s.Replace("&", " and ");
        s = NonAlnum.Replace(s, " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    public static string NormalizeFileName(string pathOrName)
    {
        var stem = Path.GetFileNameWithoutExtension(pathOrName.Replace('\\', '/'));
        // Drop leading track numbers like "01 - " / "1."
        stem = Regex.Replace(stem, @"^\d{1,3}\s*[-_.]\s*", "");
        return NormalizeToken(stem);
    }

    public static List<TrackScanInfo> ScanFolder(string folder, Action<string>? log = null, CancellationToken ct = default)
    {
        var results = new List<TrackScanInfo>();
        if (!Directory.Exists(folder)) return results;

        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => AlbumSplitter.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        log?.Invoke($"Duplicate scan: {files.Count} audio file(s) under {folder}");

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                string? artist = null, album = null, title = null;
                var duration = TimeSpan.Zero;
                try
                {
                    using var tf = TagLib.File.Create(path);
                    artist = First(tf.Tag.FirstPerformer, string.Join("; ", tf.Tag.Performers ?? Array.Empty<string>()));
                    album = tf.Tag.Album;
                    title = tf.Tag.Title;
                    duration = tf.Properties.Duration;
                }
                catch
                {
                    // unreadable tags — still usable via size/name
                }

                var scan = new TrackScanInfo
                {
                    Path = path,
                    FileName = Path.GetFileName(path),
                    FileSizeBytes = info.Length,
                    Duration = duration,
                    Artist = NullIfEmpty(artist),
                    Album = NullIfEmpty(album),
                    Title = NullIfEmpty(title),
                    NormalizedTagKey = NormalizeTagKey(artist, album, title),
                    NormalizedFileName = NormalizeFileName(path)
                };
                results.Add(scan);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Skip {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return results;
    }

    public static List<DuplicateGroup> FindDuplicates(
        IReadOnlyList<TrackScanInfo> tracks,
        AppSettings settings)
    {
        var groups = new List<DuplicateGroup>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Tag-key groups (need a non-empty key)
        foreach (var g in tracks
                     .Where(t => !string.IsNullOrEmpty(t.NormalizedTagKey))
                     .GroupBy(t => t.NormalizedTagKey!, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            var list = g.ToList();
            foreach (var t in list) claimed.Add(t.Path);
            groups.Add(new DuplicateGroup
            {
                Reason = "Same artist+album+title tags",
                Tracks = list
            });
        }

        // 2) Size + duration clusters among remaining
        var remaining = tracks.Where(t => !claimed.Contains(t.Path)).ToList();
        var sizeDurationGroups = ClusterBySizeAndDuration(remaining, settings);
        foreach (var list in sizeDurationGroups)
        {
            foreach (var t in list) claimed.Add(t.Path);
            groups.Add(new DuplicateGroup
            {
                Reason = "Similar file size + duration",
                Tracks = list
            });
        }

        // 3) Optional filename similarity among still-unclaimed
        if (settings.DuplicateUseFilenameSimilarity)
        {
            remaining = tracks.Where(t => !claimed.Contains(t.Path)).ToList();
            foreach (var g in remaining
                         .Where(t => !string.IsNullOrEmpty(t.NormalizedFileName))
                         .GroupBy(t => t.NormalizedFileName!, StringComparer.Ordinal)
                         .Where(g => g.Count() > 1))
            {
                var list = g.ToList();
                // Require at least two distinct directories or different sizes to avoid false positives
                // on legitimately same-named tracks in different albums — still report; user decides.
                groups.Add(new DuplicateGroup
                {
                    Reason = "Similar filename",
                    Tracks = list
                });
            }
        }

        return groups.OrderByDescending(g => g.Tracks.Count).ToList();
    }

    private static List<List<TrackScanInfo>> ClusterBySizeAndDuration(
        List<TrackScanInfo> tracks,
        AppSettings settings)
    {
        var result = new List<List<TrackScanInfo>>();
        var used = new bool[tracks.Count];
        var sizeTol = Math.Max(0, settings.DuplicateSizeTolerancePercent);
        var durTol = TimeSpan.FromSeconds(Math.Max(0, settings.DuplicateDurationToleranceSeconds));

        for (var i = 0; i < tracks.Count; i++)
        {
            if (used[i]) continue;
            if (tracks[i].FileSizeBytes <= 0 && tracks[i].Duration <= TimeSpan.Zero) continue;

            var cluster = new List<TrackScanInfo> { tracks[i] };
            used[i] = true;

            for (var j = i + 1; j < tracks.Count; j++)
            {
                if (used[j]) continue;
                if (SizesMatch(tracks[i], tracks[j], sizeTol) && DurationsMatch(tracks[i], tracks[j], durTol))
                {
                    cluster.Add(tracks[j]);
                    used[j] = true;
                }
            }

            if (cluster.Count > 1)
                result.Add(cluster);
        }

        return result;
    }

    private static bool SizesMatch(TrackScanInfo a, TrackScanInfo b, double tolPercent)
    {
        if (a.FileSizeBytes <= 0 || b.FileSizeBytes <= 0) return false;
        var larger = Math.Max(a.FileSizeBytes, b.FileSizeBytes);
        var smaller = Math.Min(a.FileSizeBytes, b.FileSizeBytes);
        return (larger - smaller) / (double)larger <= tolPercent;
    }

    private static bool DurationsMatch(TrackScanInfo a, TrackScanInfo b, TimeSpan tol)
    {
        if (a.Duration <= TimeSpan.Zero || b.Duration <= TimeSpan.Zero) return false;
        return (a.Duration - b.Duration).Duration() <= tol;
    }

    /// <summary>
    /// Moves files into <paramref name="quarantineRoot"/>/DuplicatesQuarantine preserving relative names.
    /// Returns paths that were moved.
    /// </summary>
    public static List<string> QuarantineFiles(
        IEnumerable<string> paths,
        string scanRoot,
        Action<string>? log = null)
    {
        var moved = new List<string>();
        var quarantine = Path.Combine(scanRoot, "DuplicatesQuarantine");
        Directory.CreateDirectory(quarantine);

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var rel = Path.GetRelativePath(scanRoot, path);
                if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                    rel = Path.GetFileName(path);
                var dest = Path.Combine(quarantine, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest))
                {
                    var stem = Path.GetFileNameWithoutExtension(dest);
                    var ext = Path.GetExtension(dest);
                    dest = Path.Combine(Path.GetDirectoryName(dest)!, $"{stem}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
                }
                File.Move(path, dest);
                moved.Add(dest);
                log?.Invoke($"Quarantined: {path} → {dest}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Could not quarantine {path}: {ex.Message}");
            }
        }

        return moved;
    }

    private static string? First(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
