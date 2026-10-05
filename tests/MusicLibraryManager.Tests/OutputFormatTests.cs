using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class OutputFormatTests
{
    [Theory]
    [InlineData(OutputFormat.Mp3, ".mp3")]
    [InlineData(OutputFormat.Flac, ".flac")]
    [InlineData(OutputFormat.Alac, ".m4a")]
    [InlineData(OutputFormat.KeepOriginal, ".wav")]
    public void CdRipper_ExtensionFor_MapsFormats(OutputFormat format, string expected)
    {
        Assert.Equal(expected, CdRipperService.ExtensionFor(format));
    }

    [Fact]
    public void Alac_IsDistinctFromFlacAndMp3()
    {
        Assert.NotEqual(OutputFormat.Alac, OutputFormat.Flac);
        Assert.NotEqual(OutputFormat.Alac, OutputFormat.Mp3);
        Assert.Equal(4, Enum.GetValues<OutputFormat>().Length);
    }
}
