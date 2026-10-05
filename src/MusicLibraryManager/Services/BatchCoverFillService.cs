using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

public enum BatchCoverAlbumStatus
{
    SkippedHasCover,
    Downloaded,
    NoReleaseFound,
    NoCoverArt,
    Error
}

public sealed class BatchCoverAlbumResult
{
    public string AlbumDirectory { get; init; } = "";
    public string AlbumName { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public BatchCoverAlbumStatus Status { get; init; }
    public string? Message { get; init; }
    public string? ReleaseId { get; init; }
}

public sealed class BatchCoverFillSummary
{
    public int Total { get; set; }
    public int Skipped { get; set; }
    public int Downloaded { get; set; }
    public int NoMatch { get; set; }
    public int Failed { get; set; }
    public List<BatchCoverAlbumResult> Results { get; } = new();
}

/// <summary>
/// Batch-fill missing cover.jpg files for album subfolders under an artist folder.
/// </summary>
public static class BatchCoverFillService
{
    public static readonly string[] CoverBaseNames = { "cover", "folder", "front", "album" };
    public static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png" };

    /// <summary>
    /// True when the album directory already has a conventional cover image
    /// (cover/folder/front/album + jpg/jpeg/png, case-insensitive).
    /// </summary>
    public static bool HasExistingCover(string albumDirectory)
    {
        if (string.IsNullOrWhiteSpace(albumDirectory) || !Directory.Exists(albumDirectory))
            return false;

        string[] files;
        try
        {
            files = Directory.GetFiles(albumDirectory, "*.*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return false;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ext))
                continue;
            if (!CoverExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                continue;
            if (CoverBaseNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Light cleanup for folder-derived names: trim, underscores→spaces, collapse whitespace.
    /// Does not strip articles or aggressively rewrite titles.
    /// </summary>
    public static string CleanFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var s = name.Trim().Replace('_', ' ');
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Trim();
    }

    /// <summary>
    /// Resolve Artist/Album for an album subfolder. Prefers tags from a primary audio file
    /// (via <see cref="AlbumSplitter.DiscoverFromAudio"/>); falls back to parent/child folder names.
    /// </summary>
    public static (string Artist, string Album) ResolveArtistAlbum(string artistFolder, string albumDirectory)
    {
        var folderArtist = CleanFolderName(Path.GetFileName(
            artistFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        var folderAlbum = CleanFolderName(Path.GetFileName(
            albumDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        string? tagArtist = null;
        string? tagAlbum = null;

        try
        {
            var primary = FindPrimaryAudio(albumDirectory);
            if (primary != null)
            {
                var job = AlbumSplitter.DiscoverFromAudio(primary);
                if (job != null)
                {
                    tagArtist = string.IsNullOrWhiteSpace(job.Artist) ? null : job.Artist.Trim();
                    tagAlbum = string.IsNullOrWhiteSpace(job.Album) ? null : job.Album.Trim();
                }
            }
        }
        catch
        {
            // ignore unreadable tags / IO
        }

        var artist = TaggingService.FirstNonEmpty(tagArtist, folderArtist, "Unknown Artist");
        var album = TaggingService.FirstNonEmpty(tagAlbum, folderAlbum, "Unknown Album");
        return (artist, album);
    }

    /// <summary>
    /// Pick best MusicBrainz candidate: highest Score when any scores are present, else first.
    /// </summary>
    public static MusicBrainzReleaseCandidate? SelectBestCandidate(
        IReadOnlyList<MusicBrainzReleaseCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;
        if (candidates.Any(c => c.Score.HasValue))
            return candidates.OrderByDescending(c => c.Score ?? int.MinValue).First();
        return candidates[0];
    }

    public static IReadOnlyList<string> GetImmediateSubdirectories(string artistFolder)
    {
        if (string.IsNullOrWhiteSpace(artistFolder) || !Directory.Exists(artistFolder))
            return Array.Empty<string>();
        return Directory.GetDirectories(artistFolder)
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Scan immediate child directories of <paramref name="artistFolder"/> and download
    /// missing covers via MusicBrainz / Cover Art Archive. Respects client rate limiting.
    /// </summary>
    public static async Task<BatchCoverFillSummary> FillMissingCoversAsync(
        string artistFolder,
        MusicBrainzClient client,
        Action<string>? log = null,
        Action<int, int>? progress = null,
        CancellationToken ct = default)
    {
        var summary = new BatchCoverFillSummary();
        var albumDirs = GetImmediateSubdirectories(artistFolder);
        summary.Total = albumDirs.Count;

        if (albumDirs.Count == 0)
        {
            log?.Invoke($"No album subfolders under: {artistFolder}");
            return summary;
        }

        log?.Invoke($"Scanning {albumDirs.Count} album folder(s) under \"{Path.GetFileName(artistFolder)}\"...");

        for (var i = 0; i < albumDirs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(i + 1, albumDirs.Count);

            var albumDir = albumDirs[i];
            var albumFolderName = Path.GetFileName(albumDir);

            if (HasExistingCover(albumDir))
            {
                summary.Skipped++;
                var skipped = new BatchCoverAlbumResult
                {
                    AlbumDirectory = albumDir,
                    AlbumName = albumFolderName,
                    Status = BatchCoverAlbumStatus.SkippedHasCover,
                    Message = "Already has cover art"
                };
                summary.Results.Add(skipped);
                log?.Invoke($"[{i + 1}/{albumDirs.Count}] SKIP  {albumFolderName} — has cover");
                continue;
            }

            var (artist, album) = ResolveArtistAlbum(artistFolder, albumDir);

            try
            {
                log?.Invoke($"[{i + 1}/{albumDirs.Count}] LOOKUP  {artist} / {album}");
                var candidates = await client.SearchReleasesAsync(artist, album, ct).ConfigureAwait(false);
                var best = SelectBestCandidate(candidates);
                if (best == null || string.IsNullOrWhiteSpace(best.ReleaseId))
                {
                    summary.NoMatch++;
                    summary.Results.Add(new BatchCoverAlbumResult
                    {
                        AlbumDirectory = albumDir,
                        AlbumName = albumFolderName,
                        Artist = artist,
                        Album = album,
                        Status = BatchCoverAlbumStatus.NoReleaseFound,
                        Message = "No MusicBrainz release found"
                    });
                    log?.Invoke($"[{i + 1}/{albumDirs.Count}] NO MATCH  {albumFolderName}");
                    continue;
                }

                var dest = Path.Combine(albumDir, "cover.jpg");
                var saved = await client.DownloadFrontCoverAsync(best.ReleaseId, dest, ct).ConfigureAwait(false);
                if (saved == null)
                {
                    summary.Failed++;
                    summary.Results.Add(new BatchCoverAlbumResult
                    {
                        AlbumDirectory = albumDir,
                        AlbumName = albumFolderName,
                        Artist = artist,
                        Album = album,
                        Status = BatchCoverAlbumStatus.NoCoverArt,
                        Message = "Release found but no cover art",
                        ReleaseId = best.ReleaseId
                    });
                    log?.Invoke($"[{i + 1}/{albumDirs.Count}] NO COVER  {albumFolderName} (release {best.ReleaseId})");
                    continue;
                }

                summary.Downloaded++;
                summary.Results.Add(new BatchCoverAlbumResult
                {
                    AlbumDirectory = albumDir,
                    AlbumName = albumFolderName,
                    Artist = artist,
                    Album = album,
                    Status = BatchCoverAlbumStatus.Downloaded,
                    Message = $"Saved cover.jpg ({best.Artist} — {best.Album})",
                    ReleaseId = best.ReleaseId
                });
                log?.Invoke($"[{i + 1}/{albumDirs.Count}] OK  {albumFolderName} → cover.jpg");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                summary.Failed++;
                summary.Results.Add(new BatchCoverAlbumResult
                {
                    AlbumDirectory = albumDir,
                    AlbumName = albumFolderName,
                    Artist = artist,
                    Album = album,
                    Status = BatchCoverAlbumStatus.Error,
                    Message = ex.Message
                });
                log?.Invoke($"[{i + 1}/{albumDirs.Count}] ERROR  {albumFolderName}: {ex.Message}");
            }
        }

        log?.Invoke(
            $"Done. Downloaded: {summary.Downloaded}, Skipped: {summary.Skipped}, " +
            $"No match: {summary.NoMatch}, Failed: {summary.Failed} (of {summary.Total}).");
        return summary;
    }

    private static string? FindPrimaryAudio(string albumDirectory)
    {
        if (!Directory.Exists(albumDirectory))
            return null;

        try
        {
            var files = Directory.EnumerateFiles(albumDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => AlbumSplitter.AudioExtensions.Contains(
                    Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (files.Count == 0)
                return null;
            return files.OrderByDescending(f => new FileInfo(f).Length).First();
        }
        catch
        {
            return null;
        }
    }
}
