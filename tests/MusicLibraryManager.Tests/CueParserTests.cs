using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class CueParserTests
{
    private const string SampleCue = """
        REM GENRE Rock
        REM DATE 1999
        REM COMMENT "Test cue"
        PERFORMER "Test Artist"
        TITLE "Test Album"
        FILE "album.flac" WAVE
          TRACK 01 AUDIO
            TITLE "First Song"
            PERFORMER "Test Artist"
            INDEX 00 00:00:00
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            TITLE "Second Song"
            PERFORMER "Guest Singer"
            INDEX 01 03:45:30
          TRACK 03 AUDIO
            TITLE "Third Song"
            INDEX 01 07:12:00
        """;

    [Fact]
    public void ParseLines_ReadsAlbumMetadataAndTracks()
    {
        var sheet = CueParser.ParseLines(SampleCue.Split('\n'));

        Assert.Equal("Test Artist", sheet.Performer);
        Assert.Equal("Test Album", sheet.Title);
        Assert.Equal("Rock", sheet.Genre);
        Assert.Equal("1999", sheet.Date);
        Assert.Equal("album.flac", sheet.FileName);
        Assert.Equal(3, sheet.Tracks.Count);

        Assert.Equal(1, sheet.Tracks[0].Number);
        Assert.Equal("First Song", sheet.Tracks[0].Title);
        Assert.Equal(TimeSpan.Zero, sheet.Tracks[0].StartTime);
        Assert.Equal(CueParser.FramesToTimeSpan(3, 45, 30), sheet.Tracks[0].EndTime);

        Assert.Equal("Second Song", sheet.Tracks[1].Title);
        Assert.Equal("Guest Singer", sheet.Tracks[1].Performer);
        Assert.Equal(CueParser.FramesToTimeSpan(3, 45, 30), sheet.Tracks[1].StartTime);
        Assert.Equal(CueParser.FramesToTimeSpan(7, 12, 0), sheet.Tracks[1].EndTime);

        Assert.Equal("Third Song", sheet.Tracks[2].Title);
        Assert.Null(sheet.Tracks[2].EndTime);
    }

    [Fact]
    public void FramesToTimeSpan_ConvertsCdFrames()
    {
        // 1 second = 75 frames
        var ts = CueParser.FramesToTimeSpan(0, 1, 0);
        Assert.Equal(TimeSpan.FromSeconds(1), ts);

        var half = CueParser.FramesToTimeSpan(0, 0, 37);
        Assert.True(Math.Abs(half.TotalSeconds - 37.0 / 75.0) < 0.001);
    }

    [Fact]
    public void ParseLines_HandlesEmptyAndMissingFields()
    {
        var cue = """
            FILE "x.wav" WAVE
              TRACK 01 AUDIO
                INDEX 01 00:00:00
            """;
        var sheet = CueParser.ParseLines(cue.Split('\n'));
        Assert.Single(sheet.Tracks);
        Assert.Equal(string.Empty, sheet.Tracks[0].Title);
    }
}
