using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class DuplicateFinderTests
{
    [Theory]
    [InlineData("The Beatles", "Abbey Road", "Come Together", "the beatles|abbey road|come together")]
    [InlineData("AC/DC", "Back in Black", "Hells Bells", "ac dc|back in black|hells bells")]
    [InlineData("Artist & Band", "Album!", "Song?", "artist and band|album|song")]
    [InlineData("  Mixed CASE  ", "  Spaces  ", "Title", "mixed case|spaces|title")]
    public void NormalizeTagKey_ProducesStableKey(string artist, string album, string title, string expected)
    {
        Assert.Equal(expected, DuplicateFinderService.NormalizeTagKey(artist, album, title));
    }

    [Fact]
    public void NormalizeTagKey_EmptyWhenAllMissing()
    {
        Assert.Equal(string.Empty, DuplicateFinderService.NormalizeTagKey(null, "  ", ""));
    }

    [Theory]
    [InlineData("01 - Come Together.mp3", "come together")]
    [InlineData("12. Hells Bells.flac", "hells bells")]
    [InlineData("Song_Name", "song name")]
    public void NormalizeFileName_StripsTrackPrefix(string name, string expected)
    {
        Assert.Equal(expected, DuplicateFinderService.NormalizeFileName(name));
    }

    [Fact]
    public void FindDuplicates_GroupsByTagKey()
    {
        var tracks = new List<TrackScanInfo>
        {
            new()
            {
                Path = "/a/1.mp3", FileName = "1.mp3", FileSizeBytes = 1000,
                Duration = TimeSpan.FromSeconds(200),
                Artist = "Band", Album = "LP", Title = "Song",
                NormalizedTagKey = DuplicateFinderService.NormalizeTagKey("Band", "LP", "Song"),
                NormalizedFileName = "song"
            },
            new()
            {
                Path = "/b/2.mp3", FileName = "2.mp3", FileSizeBytes = 1100,
                Duration = TimeSpan.FromSeconds(201),
                Artist = "Band", Album = "LP", Title = "Song",
                NormalizedTagKey = DuplicateFinderService.NormalizeTagKey("Band", "LP", "Song"),
                NormalizedFileName = "song"
            },
            new()
            {
                Path = "/c/other.mp3", FileName = "other.mp3", FileSizeBytes = 5000,
                Duration = TimeSpan.FromSeconds(90),
                Artist = "Other", Album = "X", Title = "Y",
                NormalizedTagKey = DuplicateFinderService.NormalizeTagKey("Other", "X", "Y"),
                NormalizedFileName = "other"
            }
        };

        var settings = new AppSettings();
        var groups = DuplicateFinderService.FindDuplicates(tracks, settings);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Tracks.Count);
        Assert.Contains("tags", groups[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindDuplicates_GroupsBySizeAndDuration()
    {
        var tracks = new List<TrackScanInfo>
        {
            new()
            {
                Path = "/a/x.flac", FileName = "x.flac", FileSizeBytes = 10_000_000,
                Duration = TimeSpan.FromSeconds(180),
                NormalizedTagKey = "", NormalizedFileName = "alpha"
            },
            new()
            {
                Path = "/b/y.mp3", FileName = "y.mp3", FileSizeBytes = 9_900_000,
                Duration = TimeSpan.FromSeconds(181),
                NormalizedTagKey = "", NormalizedFileName = "beta"
            }
        };

        var settings = new AppSettings
        {
            DuplicateSizeTolerancePercent = 0.05,
            DuplicateDurationToleranceSeconds = 2,
            DuplicateUseFilenameSimilarity = false
        };
        var groups = DuplicateFinderService.FindDuplicates(tracks, settings);
        Assert.Single(groups);
        Assert.Contains("size", groups[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsLikelyAlbumFile_RejectsNumberedTracks()
    {
        Assert.False(AlbumSplitter.IsLikelyAlbumFile("01 - Song.flac"));
        Assert.True(AlbumSplitter.IsLikelyAlbumFile("Artist - Album.flac"));
    }
}
