using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class CdTocParserTests
{
    [Fact]
    public void MsfToFrames_AndBack_RoundTrips()
    {
        Assert.Equal(150, CdTocParser.MsfToFrames(0, 2, 0));
        Assert.Equal(0, CdTocParser.FramesToLba(150));
        Assert.Equal(150, CdTocParser.LbaToFrames(0));
        var (m, s, f) = CdTocParser.FramesToMsf(95462);
        Assert.Equal(95462, CdTocParser.MsfToFrames(m, s, f));
    }

    [Fact]
    public void FromOffsets_ComputesLengthsAndLba()
    {
        var toc = CdTocParser.FromOffsets(1, 3, 1000, new[] { 150, 400, 700 });
        Assert.Equal(3, toc.Tracks.Count);
        Assert.Equal(0, toc.Tracks[0].StartLba);
        Assert.Equal(250, toc.Tracks[0].LengthSectors);
        Assert.Equal(300, toc.Tracks[1].LengthSectors);
        Assert.Equal(300, toc.Tracks[2].LengthSectors);
        Assert.All(toc.Tracks, t => Assert.True(t.IsAudio));
    }

    [Fact]
    public void ParseWindowsToc_WikiSixTrack()
    {
        Assert.Equal(150, CdTocParser.MsfToFrames(0, 2, 0));
        Assert.Equal(15363, CdTocParser.MsfToFrames(3, 24, 63));
        Assert.Equal(95462, CdTocParser.MsfToFrames(21, 12, 62));

        var tracks = new (int, int, int, int, bool)[]
        {
            (1, 0, 2, 0, true),
            (2, 3, 24, 63, true),
            (3, 7, 10, 64, true),
            (4, 10, 21, 17, true),
            (5, 14, 5, 39, true),
            (6, 17, 53, 14, true),
        };

        var bytes = CdTocParser.BuildWindowsTocBytes(1, 6, tracks, (21, 12, 62));
        var toc = CdTocParser.ParseWindowsToc(bytes, "D:", @"\\.\D:");
        Assert.Equal(6, toc.AudioTrackCount);
        Assert.Equal(95462, toc.LeadOutOffset);
        Assert.Equal("49HHV7Eb8UKF3aQiNmu1GR8vKTY-", DiscIdCalculator.Compute(toc));
        Assert.Equal("D:", toc.DriveLetter);
    }

    [Fact]
    public void FromOffsets_CdExtra_AdjustsLeadOut()
    {
        var offsets = new[] { 150, 14109, 33586, 53077, 65781, 77892, 99174, 125974 };
        var toc = CdTocParser.FromOffsets(1, 8, 125974, offsets, dataTrackNumbers: new[] { 8 });
        Assert.Equal(7, toc.AudioTrackCount);
        Assert.Equal(114574, toc.LeadOutOffset);
        Assert.False(toc.Tracks[^1].IsAudio);
        Assert.Equal(7, toc.LastTrack);
    }

    [Fact]
    public void SectorsToTime_Uses75Fps()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), CdTocParser.SectorsToTime(150));
    }
}
