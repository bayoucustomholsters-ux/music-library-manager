using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class AcoustIdClientTests
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
    public void ParseFpcalcJson_ReadsDurationAndFingerprint()
    {
        var json = Fixture("fpcalc-sample.json");
        var (duration, fp) = AcoustIdClient.ParseFpcalcJson(json);
        Assert.Equal(183, duration);
        Assert.StartsWith("AQADtEmUREk", fp);
    }

    [Fact]
    public void ParseLookupResponse_MapsReleases()
    {
        var json = Fixture("acoustid-lookup.json");
        var result = AcoustIdClient.ParseLookupResponse(json);
        Assert.Equal("ok", result.Status);
        Assert.Single(result.Results);
        Assert.Equal(0.98, result.Results[0].Score);
        Assert.Single(result.Results[0].Recordings);
        Assert.Equal("Nirvana", result.Results[0].Recordings[0].Artist);
        Assert.Equal(2, result.Results[0].Recordings[0].Releases.Count);
    }

    [Fact]
    public void ToMusicBrainzCandidates_DedupesByReleaseId()
    {
        var json = Fixture("acoustid-lookup.json");
        var result = AcoustIdClient.ParseLookupResponse(json);
        var candidates = AcoustIdClient.ToMusicBrainzCandidates(result);
        Assert.Equal(2, candidates.Count);
        Assert.Equal("bbbbbbbb-cccc-dddd-eeee-ffffffffffff", candidates[0].ReleaseId);
        Assert.Equal("Nirvana", candidates[0].Artist);
        Assert.Equal("Nevermind", candidates[0].Album);
        Assert.Equal("1991", candidates[0].Year);
        Assert.Contains("AcoustID score", candidates[0].Disambiguation);
    }
}
