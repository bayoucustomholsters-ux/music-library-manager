using System.Security.Cryptography;
using System.Text;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// MusicBrainz DiscID (libdiscid algorithm). SHA-1 of uppercase hex TOC, then
/// URL-safe Base64 with + → .  / → _  = → -
/// See https://musicbrainz.org/doc/Disc_ID_Calculation
/// </summary>
public static class DiscIdCalculator
{
    public const int MaxTracks = 99;

    /// <summary>
    /// Compute DiscID from first/last track numbers and a 100-slot frame-offset table
    /// where index 0 is lead-out and indices 1–99 are per-track offsets (0 if unused).
    /// Track numbers are 1-based; a disc whose first audio track is 2 leaves index 1 as 0.
    /// </summary>
    public static string Compute(int firstTrack, int lastTrack, int[] frameOffsets100)
    {
        if (firstTrack < 1 || firstTrack > MaxTracks)
            throw new ArgumentOutOfRangeException(nameof(firstTrack));
        if (lastTrack < firstTrack || lastTrack > MaxTracks)
            throw new ArgumentOutOfRangeException(nameof(lastTrack));
        ArgumentNullException.ThrowIfNull(frameOffsets100);
        if (frameOffsets100.Length < 100)
            throw new ArgumentException("Frame offset table must have 100 entries (lead-out + 99 tracks).", nameof(frameOffsets100));

        var sb = new StringBuilder(2 + 2 + 100 * 8);
        sb.Append(firstTrack.ToString("X2"));
        sb.Append(lastTrack.ToString("X2"));
        for (var i = 0; i < 100; i++)
            sb.Append(frameOffsets100[i].ToString("X8"));

        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
        var b64 = Convert.ToBase64String(hash);
        return b64.Replace('+', '.').Replace('/', '_').Replace('=', '-');
    }

    /// <summary>
    /// Convenience: first/last audio track, lead-out offset, and per-track offsets
    /// for tracks firstTrack..lastTrack (length = last - first + 1).
    /// </summary>
    public static string Compute(int firstTrack, int lastTrack, int leadOutOffset, IReadOnlyList<int> trackOffsets)
    {
        ArgumentNullException.ThrowIfNull(trackOffsets);
        var expected = lastTrack - firstTrack + 1;
        if (trackOffsets.Count != expected)
            throw new ArgumentException($"Expected {expected} track offset(s) for tracks {firstTrack}–{lastTrack}, got {trackOffsets.Count}.");

        var table = new int[100];
        table[0] = leadOutOffset;
        for (var i = 0; i < trackOffsets.Count; i++)
            table[firstTrack + i] = trackOffsets[i];
        return Compute(firstTrack, lastTrack, table);
    }

    public static string Compute(CdToc toc)
    {
        ArgumentNullException.ThrowIfNull(toc);
        var audio = toc.Tracks.Where(t => t.IsAudio).OrderBy(t => t.Number).ToList();
        if (audio.Count == 0)
            throw new InvalidOperationException("TOC has no audio tracks — cannot compute a MusicBrainz DiscID.");

        var first = audio[0].Number;
        var last = audio[^1].Number;
        var offsets = audio.Select(t => t.OffsetFrames).ToList();
        return Compute(first, last, toc.LeadOutOffset, offsets);
    }

    /// <summary>
    /// MusicBrainz TOC query string: first last leadout off1 off2 ...
    /// </summary>
    public static string ToTocQuery(CdToc toc)
    {
        ArgumentNullException.ThrowIfNull(toc);
        var audio = toc.Tracks.Where(t => t.IsAudio).OrderBy(t => t.Number).ToList();
        if (audio.Count == 0)
            throw new InvalidOperationException("TOC has no audio tracks.");

        var parts = new List<string>
        {
            audio[0].Number.ToString(),
            audio[^1].Number.ToString(),
            toc.LeadOutOffset.ToString()
        };
        parts.AddRange(audio.Select(t => t.OffsetFrames.ToString()));
        return string.Join(" ", parts);
    }

    public static string ToTocQueryUrl(CdToc toc) => ToTocQuery(toc).Replace(' ', '+');
}
