using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class DiscogsClientTests
{
    private static string Fixture(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fixtures", name),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name)),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return File.ReadAllText(c);
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void ParseSearchResults_ReadsCandidates()
    {
        var json = Fixture("discogs-search.json");
        var results = DiscogsClient.ParseSearchResults(json);
        Assert.Equal(2, results.Count);
        Assert.Equal(123456, results[0].Id);
        Assert.Equal("Nirvana", results[0].Artist);
        Assert.Equal("Nevermind", results[0].Album);
        Assert.Equal("1991", results[0].Year);
        Assert.Equal("Rock", results[0].Genre);
        Assert.Contains("1991", results[0].DisplayLabel);
    }

    [Fact]
    public void ParseReleaseDetail_ReadsTracks()
    {
        var json = Fixture("discogs-release.json");
        var detail = DiscogsClient.ParseReleaseDetail(json);
        Assert.NotNull(detail);
        Assert.Equal("Nirvana", detail!.Artist);
        Assert.Equal("Nevermind", detail.Album);
        Assert.Equal("1991", detail.Year);
        Assert.Equal("Rock", detail.Genre);
        Assert.Equal(3, detail.Tracks.Count);
        Assert.Equal("Smells Like Teen Spirit", detail.Tracks[0].Title);
        Assert.Equal(1, detail.Tracks[0].Number);
        Assert.Equal("Come As You Are", detail.Tracks[2].Title);
    }

    [Fact]
    public void SplitDiscogsTitle_ParsesArtistAlbum()
    {
        var (artist, album) = DiscogsClient.SplitDiscogsTitle("Pink Floyd - The Wall");
        Assert.Equal("Pink Floyd", artist);
        Assert.Equal("The Wall", album);
    }
}
