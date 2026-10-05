using MusicLibraryManager.Models;
using TagLib;
using File = TagLib.File;

namespace MusicLibraryManager.Services;

public static class TaggingService
{
    /// <summary>
    /// Reads common tags and embedded pictures from an audio file.
    /// Picture data is copied so it remains valid after the file is closed.
    /// </summary>
    public static AudioTagInfo ReadTags(string path)
    {
        using var file = File.Create(path);
        var tag = file.Tag;

        string? Join(string[]? values) =>
            values is { Length: > 0 } && values.Any(v => !string.IsNullOrWhiteSpace(v))
                ? string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()))
                : null;

        List<EmbeddedPicture>? pictures = null;
        if (tag.Pictures is { Length: > 0 })
        {
            pictures = new List<EmbeddedPicture>(tag.Pictures.Length);
            foreach (var p in tag.Pictures)
            {
                if (p.Data is null || p.Data.Count == 0) continue;
                pictures.Add(new EmbeddedPicture
                {
                    MimeType = p.MimeType,
                    Description = p.Description,
                    Type = (int)p.Type,
                    Data = p.Data.ToArray()
                });
            }
            if (pictures.Count == 0) pictures = null;
        }

        return new AudioTagInfo
        {
            Title = NullIfEmpty(tag.Title),
            Artist = Join(tag.Performers),
            AlbumArtist = Join(tag.AlbumArtists),
            Album = NullIfEmpty(tag.Album),
            Year = tag.Year > 0 ? tag.Year.ToString() : null,
            Genre = Join(tag.Genres),
            Track = tag.Track > 0 ? tag.Track : null,
            TrackCount = tag.TrackCount > 0 ? tag.TrackCount : null,
            Disc = tag.Disc > 0 ? tag.Disc : null,
            DiscCount = tag.DiscCount > 0 ? tag.DiscCount : null,
            Pictures = pictures
        };
    }

    /// <summary>
    /// Merges source-file tags with explicit overrides. Non-empty overrides win
    /// (UI / CUE / MusicBrainz). When <paramref name="preserveSource"/> is false,
    /// source values are ignored (only overrides + defaults apply).
    /// </summary>
    public static AudioTagInfo MergeTags(
        AudioTagInfo? source,
        string? overrideTitle = null,
        string? overrideArtist = null,
        string? overrideAlbumArtist = null,
        string? overrideAlbum = null,
        string? overrideYear = null,
        string? overrideGenre = null,
        int? overrideTrack = null,
        int? overrideTrackCount = null,
        int? overrideDisc = null,
        int? overrideDiscCount = null,
        bool preserveSource = true)
    {
        var s = preserveSource ? source : null;

        var artist = FirstNonEmpty(overrideArtist, s?.Artist);
        var albumArtist = FirstNonEmpty(overrideAlbumArtist, s?.AlbumArtist, artist);

        uint? track = overrideTrack is > 0 ? (uint)overrideTrack.Value : s?.Track;
        uint? trackCount = overrideTrackCount is > 0 ? (uint)overrideTrackCount.Value : s?.TrackCount;
        uint? disc = overrideDisc is > 0 ? (uint)overrideDisc.Value : s?.Disc;
        uint? discCount = overrideDiscCount is > 0 ? (uint)overrideDiscCount.Value : s?.DiscCount;

        return new AudioTagInfo
        {
            Title = FirstNonEmpty(overrideTitle, s?.Title),
            Artist = artist,
            AlbumArtist = albumArtist,
            Album = FirstNonEmpty(overrideAlbum, s?.Album),
            Year = NormalizeYear(FirstNonEmpty(overrideYear, s?.Year)),
            Genre = FirstNonEmpty(overrideGenre, s?.Genre),
            Track = track,
            TrackCount = trackCount,
            Disc = disc,
            DiscCount = discCount,
            Pictures = s?.Pictures
        };
    }

    /// <summary>
    /// Writes tags to an output audio file. Prefer <paramref name="coverPath"/> when set;
    /// otherwise embed <paramref name="tags"/>.Pictures if present.
    /// </summary>
    public static void ApplyTags(
        string audioPath,
        string artist,
        string album,
        string title,
        int trackNumber,
        int trackCount,
        string? year,
        string? genre,
        string? coverPath)
    {
        ApplyTags(audioPath, new AudioTagInfo
        {
            Artist = artist,
            AlbumArtist = artist,
            Album = album,
            Title = title,
            Track = trackNumber > 0 ? (uint)trackNumber : null,
            TrackCount = trackCount > 0 ? (uint)trackCount : null,
            Year = year,
            Genre = genre
        }, coverPath);
    }

    public static void ApplyTags(string audioPath, AudioTagInfo tags, string? coverPath = null)
    {
        using var file = File.Create(audioPath);

        if (!string.IsNullOrWhiteSpace(tags.Artist))
            file.Tag.Performers = new[] { tags.Artist };
        if (!string.IsNullOrWhiteSpace(tags.AlbumArtist))
            file.Tag.AlbumArtists = new[] { tags.AlbumArtist };
        else if (!string.IsNullOrWhiteSpace(tags.Artist))
            file.Tag.AlbumArtists = new[] { tags.Artist };

        if (!string.IsNullOrWhiteSpace(tags.Album))
            file.Tag.Album = tags.Album;
        if (!string.IsNullOrWhiteSpace(tags.Title))
            file.Tag.Title = tags.Title;
        if (tags.Track is > 0)
            file.Tag.Track = tags.Track.Value;
        if (tags.TrackCount is > 0)
            file.Tag.TrackCount = tags.TrackCount.Value;
        if (tags.Disc is > 0)
            file.Tag.Disc = tags.Disc.Value;
        if (tags.DiscCount is > 0)
            file.Tag.DiscCount = tags.DiscCount.Value;

        var year = NormalizeYear(tags.Year);
        if (!string.IsNullOrWhiteSpace(year) && uint.TryParse(year, out var y))
            file.Tag.Year = y;
        if (!string.IsNullOrWhiteSpace(tags.Genre))
            file.Tag.Genres = new[] { tags.Genre };

        if (!string.IsNullOrWhiteSpace(coverPath) && System.IO.File.Exists(coverPath))
        {
            var pic = new Picture(coverPath)
            {
                Type = PictureType.FrontCover,
                Description = "Cover"
            };
            file.Tag.Pictures = new IPicture[] { pic };
        }
        else if (tags.Pictures is { Count: > 0 })
        {
            file.Tag.Pictures = tags.Pictures.Select(ToPicture).ToArray();
        }

        file.Save();
    }

    private static IPicture ToPicture(EmbeddedPicture p)
    {
        return new Picture
        {
            Type = (PictureType)p.Type,
            MimeType = p.MimeType ?? "image/jpeg",
            Description = p.Description ?? "Cover",
            Data = new ByteVector(p.Data)
        };
    }

    public static string? NormalizeYear(string? year)
    {
        if (string.IsNullOrWhiteSpace(year)) return null;
        var digits = new string(year.Where(char.IsDigit).Take(4).ToArray());
        return digits.Length == 4 ? digits : NullIfEmpty(year.Trim());
    }

    public static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return "";
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Writes ReplayGain track gain/peak tags (REPLAYGAIN_TRACK_GAIN / REPLAYGAIN_TRACK_PEAK)
    /// using format-appropriate TagLib frames. Does not remove existing pictures or other tags.
    /// </summary>
    public static void ApplyReplayGainTags(string audioPath, ReplayGainInfo info)
    {
        using var file = File.Create(audioPath);
        var gain = info.TrackGainTag;
        var peak = info.TrackPeakTag;
        var wrote = false;

        // Prefer Xiph/FLAC-style fields when available; also set ID3 TXXX for MP3.
        try
        {
            var id3 = file.GetTag(TagLib.TagTypes.Id3v2, true) as TagLib.Id3v2.Tag;
            if (id3 != null)
            {
                SetUserText(id3, "REPLAYGAIN_TRACK_GAIN", gain);
                SetUserText(id3, "REPLAYGAIN_TRACK_PEAK", peak);
                wrote = true;
            }
        }
        catch { /* container may not support ID3 */ }

        try
        {
            var xiph = file.GetTag(TagLib.TagTypes.Xiph, true) as TagLib.Ogg.XiphComment;
            if (xiph != null)
            {
                xiph.SetField("REPLAYGAIN_TRACK_GAIN", gain);
                xiph.SetField("REPLAYGAIN_TRACK_PEAK", peak);
                wrote = true;
            }
        }
        catch { /* ignore */ }

        // APE tags (some MP3 / WavPack)
        try
        {
            var ape = file.GetTag(TagLib.TagTypes.Ape, true) as TagLib.Ape.Tag;
            if (ape != null)
            {
                ape.SetValue("REPLAYGAIN_TRACK_GAIN", gain);
                ape.SetValue("REPLAYGAIN_TRACK_PEAK", peak);
                wrote = true;
            }
        }
        catch { /* ignore */ }

        // MPEG-4 / ALAC (.m4a): freeform ----:com.apple.iTunes:… or dash boxes via AppleTag
        try
        {
            if (file.GetTag(TagLib.TagTypes.Apple, true) is TagLib.Mpeg4.AppleTag apple)
            {
                apple.SetDashBox("com.apple.iTunes", "REPLAYGAIN_TRACK_GAIN", gain);
                apple.SetDashBox("com.apple.iTunes", "REPLAYGAIN_TRACK_PEAK", peak);
                wrote = true;
            }
        }
        catch
        {
            // Best-effort — some TagLib builds / files reject freeform boxes
        }

        if (!wrote)
            throw new NotSupportedException(
                "ReplayGain tags are not supported for this container (e.g. some M4A/ALAC files).");

        file.Save();
    }

    private static void SetUserText(TagLib.Id3v2.Tag id3, string description, string value)
    {
        // Remove existing frames with same description
        foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>()
                     .Where(f => string.Equals(f.Description, description, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            id3.RemoveFrame(frame);
        }

        var utf = new TagLib.Id3v2.UserTextInformationFrame(description, TagLib.StringType.UTF8)
        {
            Text = new[] { value }
        };
        id3.AddFrame(utf);
    }
}
