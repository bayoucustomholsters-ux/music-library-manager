using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class ReplayGainServiceTests
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
    public void ParseLoudnormJson_ComputesGainRelativeToMinus18()
    {
        var text = Fixture("loudnorm-sample.txt");
        var info = ReplayGainService.ParseLoudnormJson(text);
        Assert.NotNull(info);
        // gain = -18 - (-14.52) = -3.48
        Assert.Equal(-3.48, info!.TrackGainDb, 2);
        Assert.True(info.TrackPeak > 0.8 && info.TrackPeak < 1.0);
        Assert.Contains("dB", info.TrackGainTag);
    }

    [Fact]
    public void ExtractJsonObject_FindsInputI()
    {
        var text = Fixture("loudnorm-sample.txt");
        var json = ReplayGainService.ExtractJsonObject(text);
        Assert.NotNull(json);
        Assert.Contains("input_i", json!);
    }
}
