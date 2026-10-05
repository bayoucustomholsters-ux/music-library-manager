using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class DiscIdCalculatorTests
{
    // Guano Apes – Don't Give Me Names (python-discid / libdiscid fixture)
    private static readonly int[] GuanoOffsets =
    {
        150, 17510, 33275, 45910, 57805, 78310, 94650, 109580,
        132010, 149160, 165115, 177710, 203325, 215555, 235590
    };

    [Fact]
    public void Compute_GuanoApes_MatchesLibdiscid()
    {
        var id = DiscIdCalculator.Compute(1, 15, 258725, GuanoOffsets);
        Assert.Equal("TqvKjMu7dMliSfmVEBtrL7sBSno-", id);
    }

    [Fact]
    public void Compute_MusicBrainzWikiSixTrack_MatchesDocumentedId()
    {
        var offsets = new[] { 150, 15363, 32314, 46592, 63414, 80489 };
        var id = DiscIdCalculator.Compute(1, 6, 95462, offsets);
        Assert.Equal("49HHV7Eb8UKF3aQiNmu1GR8vKTY-", id);
    }

    [Fact]
    public void Compute_FirstTrackTwo_MatchesLibdiscid()
    {
        var offsets = new[] { 150, 11512, 34143, 50747, 63640, 98491, 123534, 174410, 195438, 201127 };
        var id = DiscIdCalculator.Compute(2, 11, 225781, offsets);
        Assert.Equal("6RDuz0d7.M5SVMLe1z4DP0yaEC8-", id);
    }

    [Fact]
    public void Compute_FromCdToc_MatchesOffsetsApi()
    {
        var toc = CdTocParser.FromOffsets(1, 15, 258725, GuanoOffsets);
        Assert.Equal(15, toc.AudioTrackCount);
        Assert.Equal(258725, toc.LeadOutOffset);
        Assert.Equal("TqvKjMu7dMliSfmVEBtrL7sBSno-", DiscIdCalculator.Compute(toc));
    }

    [Fact]
    public void ToTocQuery_FormatsSpaceSeparated()
    {
        var toc = CdTocParser.FromOffsets(1, 6, 95462,
            new[] { 150, 15363, 32314, 46592, 63414, 80489 });
        Assert.Equal("1 6 95462 150 15363 32314 46592 63414 80489", DiscIdCalculator.ToTocQuery(toc));
    }
}
