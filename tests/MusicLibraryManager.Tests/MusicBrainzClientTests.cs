using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class MusicBrainzClientTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParseSearchResults_ReadsCandidates()
    {
        // Prefer embedded fixture next to source when not copied to output
        var json = TryReadFixture("mb-search-snippet.json");
        var results = MusicBrainzClient.ParseSearchResults(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", results[0].ReleaseId);
        Assert.Equal("Nirvana", results[0].Artist);
        Assert.Equal("Nevermind", results[0].Album);
        Assert.Equal("1991", results[0].Year);
        Assert.Equal(12, results[0].TrackCount);
        Assert.Equal("US", results[0].Country);

        Assert.Equal(20, results[1].TrackCount);
        Assert.Equal("deluxe edition", results[1].Disambiguation);
        Assert.Contains("20 tracks", results[1].DisplayLabel);
    }

    [Fact]
    public void ParseReleaseDetail_ReadsTracksAndGenre()
    {
        var json = TryReadFixture("mb-release-detail.json");
        var detail = MusicBrainzClient.ParseReleaseDetail(json);

        Assert.NotNull(detail);
        Assert.Equal("Nirvana", detail!.Artist);
        Assert.Equal("Nevermind", detail.Album);
        Assert.Equal("1991", detail.Year);
        Assert.Equal("alternative rock", detail.Genre); // release tags preferred
        Assert.Equal(3, detail.Tracks.Count);
        Assert.Equal("Smells Like Teen Spirit", detail.Tracks[0].Title);
        Assert.Equal(1, detail.Tracks[0].Number);
        Assert.Equal(301000, detail.Tracks[0].LengthMs);
        Assert.Equal("Come As You Are", detail.Tracks[2].Title);
    }

    [Fact]
    public void GuessFromFilename_ParsesArtistAlbum()
    {
        var (artist, album) = MusicBrainzClient.GuessFromFilename(@"C:\music\Pink Floyd - The Wall.flac");
        Assert.Equal("Pink Floyd", artist);
        Assert.Equal("The Wall", album);
    }

    [Fact]
    public void ExtractYear_TakesFirstFourDigits()
    {
        Assert.Equal("1991", MusicBrainzClient.ExtractYear("1991-09-24"));
        Assert.Equal("2011", MusicBrainzClient.ExtractYear("2011"));
        Assert.Null(MusicBrainzClient.ExtractYear(""));
        Assert.Null(MusicBrainzClient.ExtractYear(null));
    }

    [Fact]
    public void BuildEqualDurationSegments_SplitsEvenly()
    {
        var titles = new List<MusicBrainzTrackInfo>
        {
            new() { Number = 1, Title = "A" },
            new() { Number = 2, Title = "B" },
            new() { Number = 3, Title = "C" },
            new() { Number = 4, Title = "D" }
        };
        var total = TimeSpan.FromMinutes(40);
        var segs = AlbumSplitter.BuildEqualDurationSegments(total, titles, "Artist");

        Assert.Equal(4, segs.Count);
        Assert.Equal(TimeSpan.Zero, segs[0].start);
        Assert.Equal(TimeSpan.FromMinutes(10), segs[0].end);
        Assert.Equal(TimeSpan.FromMinutes(10), segs[1].start);
        Assert.Equal(TimeSpan.FromMinutes(20), segs[1].end);
        Assert.Null(segs[3].end); // last to EOF
        Assert.Equal("D", segs[3].title);
    }


    [Fact]
    public void ParseDiscIdResults_ReadsReleaseAndTracks()
    {
        var json = TryReadFixture("mb-discid.json");
        var results = MusicBrainzClient.ParseDiscIdResults(json);
        Assert.Single(results);
        Assert.Equal("Guano Apes", results[0].Artist);
        Assert.Equal("Don't Give Me Names", results[0].Album);
        Assert.Equal("2000", results[0].Year);
        Assert.Equal(2, results[0].Tracks.Count);
        Assert.Equal("Innocent", results[0].Tracks[0].Title);
    }

    private static string TryReadFixture(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fixtures", name),
            Path.Combine(AppContext.BaseDirectory, name),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name)),
            Path.GetFullPath(Path.Combine("/workspace/music-library-manager/tests/MusicLibraryManager.Tests/Fixtures", name))
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return File.ReadAllText(c);
        }
        throw new FileNotFoundException($"Fixture not found: {name}");
    }
}
