using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class FilenameSanitizerTests
{
    [Theory]
    [InlineData("Hello/World", "Hello_World")]
    [InlineData("Song: Title?", "Song_ Title_")]
    [InlineData("  spaced  name  ", "spaced name")]
    [InlineData("", "Unknown")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    public void Sanitize_RemovesInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, FilenameSanitizer.Sanitize(input));
    }

    [Fact]
    public void ApplyPattern_SubstitutesTokens()
    {
        var values = new Dictionary<string, string>
        {
            ["track"] = "3",
            ["title"] = "My Song",
            ["artist"] = "Band",
            ["album"] = "LP",
            ["year"] = "2001"
        };

        var name = FilenameSanitizer.ApplyPattern("{track:00} - {title}", values);
        Assert.Equal("03 - My Song", name);

        var folder = FilenameSanitizer.ApplyPattern("{artist}\\{album} ({year})", values);
        Assert.Equal("Band\\LP (2001)", folder);
    }

    [Fact]
    public void Sanitize_FallbackWhenOnlyInvalid()
    {
        Assert.Equal("Unknown", FilenameSanitizer.Sanitize("***"));
    }
}
