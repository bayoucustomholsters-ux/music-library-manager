using System.Globalization;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Parses a CD table of contents from Windows CDROM_TOC bytes, MSF values, or
/// a simple numeric fixture. No hardware required — used by the ripper and tests.
/// </summary>
public static class CdTocParser
{
    public const int FramesPerSecond = 75;
    public const int PregapFrames = 150;
    /// <summary>Standard CD-Extra gap between last audio track and first data track (2:32.00).</summary>
    public const int CdExtraGapFrames = 11400;
    public const byte LeadOutTrackNumber = 0xAA;
    public const int WindowsTocHeaderSize = 4;
    public const int TrackDataSize = 8;
    public const int WindowsTocMaxTracks = 100;
    public const int WindowsTocSize = WindowsTocHeaderSize + WindowsTocMaxTracks * TrackDataSize; // 804

    public static int MsfToFrames(int minutes, int seconds, int frames)
    {
        if (minutes < 0 || seconds < 0 || frames < 0)
            throw new ArgumentOutOfRangeException("MSF components must be non-negative.");
        return (minutes * 60 + seconds) * FramesPerSecond + frames;
    }

    public static (int Minutes, int Seconds, int Frames) FramesToMsf(int frames)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var m = frames / (FramesPerSecond * 60);
        var rem = frames % (FramesPerSecond * 60);
        var s = rem / FramesPerSecond;
        var f = rem % FramesPerSecond;
        return (m, s, f);
    }

    public static int FramesToLba(int frames) => frames - PregapFrames;

    public static int LbaToFrames(int lba) => lba + PregapFrames;

    public static TimeSpan SectorsToTime(int sectors) =>
        TimeSpan.FromSeconds(sectors / (double)FramesPerSecond);

    /// <summary>
    /// Build a TOC from first/last track, lead-out offset (frames), and per-track
    /// offsets (frames). Optional <paramref name="dataTrackNumbers"/> mark data tracks.
    /// When the last session is data (CD-Extra), pass the raw data-track offset as
    /// lead-out and set <paramref name="adjustCdExtraLeadOut"/> (default true) so
    /// DiscID uses dataOffset − 11400.
    /// </summary>
    public static CdToc FromOffsets(
        int firstTrack,
        int lastTrack,
        int leadOutOffset,
        IReadOnlyList<int> trackOffsets,
        IReadOnlyCollection<int>? dataTrackNumbers = null,
        bool adjustCdExtraLeadOut = true,
        string? driveLetter = null,
        string? devicePath = null)
    {
        if (trackOffsets.Count != lastTrack - firstTrack + 1)
            throw new ArgumentException("Track offset count must equal last − first + 1.");

        var data = dataTrackNumbers != null
            ? new HashSet<int>(dataTrackNumbers)
            : new HashSet<int>();

        var raw = new List<(int num, int off, bool audio)>();
        for (var i = 0; i < trackOffsets.Count; i++)
        {
            var num = firstTrack + i;
            raw.Add((num, trackOffsets[i], !data.Contains(num)));
        }

        var discIdLeadOut = leadOutOffset;
        var discIdLast = lastTrack;
        if (adjustCdExtraLeadOut && raw.Count > 0 && !raw[^1].audio)
        {
            var lastAudio = raw.LastOrDefault(t => t.audio);
            if (lastAudio.num != 0)
            {
                discIdLast = lastAudio.num;
                discIdLeadOut = raw[^1].off - CdExtraGapFrames;
                if (discIdLeadOut < lastAudio.off)
                    discIdLeadOut = leadOutOffset; // malformed; keep raw
            }
        }

        var tracks = new List<CdTrack>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var (num, off, isAudio) = raw[i];
            var nextOff = i + 1 < raw.Count ? raw[i + 1].off : leadOutOffset;
            // For DiscID lead-out on last audio track of a CD-Extra disc, length
            // should end at the session gap (discIdLeadOut), not the data track.
            if (isAudio && i + 1 < raw.Count && !raw[i + 1].audio && discIdLeadOut < nextOff && discIdLeadOut > off)
                nextOff = discIdLeadOut;
            var length = Math.Max(0, nextOff - off);
            tracks.Add(new CdTrack
            {
                Number = num,
                OffsetFrames = off,
                StartLba = FramesToLba(off),
                LengthSectors = length,
                IsAudio = isAudio
            });
        }

        var audioTracks = tracks.Where(t => t.IsAudio).ToList();
        return new CdToc
        {
            FirstTrack = audioTracks.Count > 0 ? audioTracks[0].Number : firstTrack,
            LastTrack = audioTracks.Count > 0 ? audioTracks[^1].Number : discIdLast,
            LeadOutOffset = discIdLeadOut,
            Tracks = tracks,
            DriveLetter = driveLetter,
            DevicePath = devicePath
        };
    }

    /// <summary>
    /// Parse a Windows <c>CDROM_TOC</c> buffer (804 bytes typical) from
    /// <c>IOCTL_CDROM_READ_TOC</c> (MSF addresses).
    /// </summary>
    public static CdToc ParseWindowsToc(ReadOnlySpan<byte> buffer, string? driveLetter = null, string? devicePath = null)
    {
        if (buffer.Length < WindowsTocHeaderSize)
            throw new ArgumentException("TOC buffer too small.");

        var first = buffer[2];
        var last = buffer[3];
        if (first == 0 || last == 0 || last < first)
            throw new InvalidOperationException("Drive returned an empty or invalid TOC (no disc, or not an audio CD).");

        var entries = new List<(int num, int frames, bool audio, bool leadOut)>();
        var offset = WindowsTocHeaderSize;
        var maxEntries = Math.Min(WindowsTocMaxTracks, (buffer.Length - WindowsTocHeaderSize) / TrackDataSize);
        for (var i = 0; i < maxEntries && offset + TrackDataSize <= buffer.Length; i++, offset += TrackDataSize)
        {
            var controlAdr = buffer[offset + 1];
            var control = (byte)(controlAdr & 0x0F); // low nibble
            var trackNumber = buffer[offset + 2];
            var m = buffer[offset + 5];
            var s = buffer[offset + 6];
            var f = buffer[offset + 7];
            var frames = MsfToFrames(m, s, f);
            var isData = (control & 0x04) != 0;
            var isLeadOut = trackNumber == LeadOutTrackNumber;
            entries.Add((trackNumber, frames, !isData && !isLeadOut, isLeadOut));
            if (isLeadOut)
                break;
            if (trackNumber >= last && entries.Count >= (last - first + 1))
            {
                // next entry should be lead-out; keep looping until AA or end
            }
        }

        var lead = entries.FirstOrDefault(e => e.leadOut);
        if (lead.frames == 0 && lead.num == 0)
            throw new InvalidOperationException("TOC is missing the lead-out track.");

        var trackEntries = entries.Where(e => !e.leadOut && e.num >= first && e.num <= last).ToList();
        if (trackEntries.Count == 0)
            throw new InvalidOperationException("TOC contains no tracks.");

        var dataNums = trackEntries.Where(e => !e.audio).Select(e => e.num).ToList();
        return FromOffsets(
            trackEntries[0].num,
            trackEntries[^1].num,
            lead.frames,
            trackEntries.Select(e => e.frames).ToList(),
            dataNums,
            adjustCdExtraLeadOut: true,
            driveLetter,
            devicePath);
    }

    /// <summary>Build a Windows CDROM_TOC byte buffer (for tests / fixtures).</summary>
    public static byte[] BuildWindowsTocBytes(
        int firstTrack,
        int lastTrack,
        IReadOnlyList<(int Number, int Minutes, int Seconds, int Frames, bool IsAudio)> tracks,
        (int Minutes, int Seconds, int Frames) leadOut)
    {
        var buf = new byte[WindowsTocSize];
        var entryCount = tracks.Count + 1; // + lead-out
        var length = 2 + entryCount * TrackDataSize;
        buf[0] = (byte)((length >> 8) & 0xFF);
        buf[1] = (byte)(length & 0xFF);
        buf[2] = (byte)firstTrack;
        buf[3] = (byte)lastTrack;

        void WriteEntry(int index, int number, int m, int s, int f, bool audio)
        {
            var o = WindowsTocHeaderSize + index * TrackDataSize;
            buf[o + 0] = 0;
            // ADR in high nibble (1 = position), Control in low nibble (4 = data)
            var control = audio ? (byte)0x00 : (byte)0x04;
            buf[o + 1] = (byte)(0x10 | control);
            buf[o + 2] = (byte)number;
            buf[o + 3] = 0;
            buf[o + 4] = 0;
            buf[o + 5] = (byte)m;
            buf[o + 6] = (byte)s;
            buf[o + 7] = (byte)f;
        }

        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            WriteEntry(i, t.Number, t.Minutes, t.Seconds, t.Frames, t.IsAudio);
        }
        WriteEntry(tracks.Count, LeadOutTrackNumber, leadOut.Minutes, leadOut.Seconds, leadOut.Frames, audio: false);
        return buf;
    }
}
