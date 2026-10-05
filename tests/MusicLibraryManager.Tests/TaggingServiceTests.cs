using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class TaggingServiceTests
{
    [Fact]
    public void MergeTags_OverridesWinOverSource()
    {
        var source = new AudioTagInfo
        {
            Title = "Source Title",
            Artist = "Source Artist",
            AlbumArtist = "Source AlbumArtist",
            Album = "Source Album",
            Year = "1999",
            Genre = "Rock",
            Track = 2,
            TrackCount = 10,
            Disc = 1,
            DiscCount = 2
        };

        var merged = TaggingService.MergeTags(
            source,
            overrideTitle: "CUE Title",
            overrideArtist: "CUE Performer",
            overrideAlbumArtist: "Job Artist",
            overrideAlbum: "Job Album",
            overrideYear: "2020",
            overrideGenre: "Jazz",
            overrideTrack: 5,
            overrideTrackCount: 12,
            preserveSource: true);

        Assert.Equal("CUE Title", merged.Title);
        Assert.Equal("CUE Performer", merged.Artist);
        Assert.Equal("Job Artist", merged.AlbumArtist);
        Assert.Equal("Job Album", merged.Album);
        Assert.Equal("2020", merged.Year);
        Assert.Equal("Jazz", merged.Genre);
        Assert.Equal(5u, merged.Track);
        Assert.Equal(12u, merged.TrackCount);
        Assert.Equal(1u, merged.Disc); // preserved from source (no override)
        Assert.Equal(2u, merged.DiscCount);
    }

    [Fact]
    public void MergeTags_FallsBackToSourceWhenOverrideEmpty()
    {
        var source = new AudioTagInfo
        {
            Title = "Keep Me",
            Artist = "Source Band",
            Album = "Source LP",
            Year = "1985-06-01",
            Genre = "Metal",
            Track = 3,
            TrackCount = 8,
            Disc = 2
        };

        var merged = TaggingService.MergeTags(
            source,
            overrideTitle: null,
            overrideArtist: "  ",
            overrideAlbum: "",
            overrideYear: null,
            overrideGenre: null,
            overrideTrack: 1,
            overrideTrackCount: 8,
            preserveSource: true);

        Assert.Equal("Keep Me", merged.Title);
        Assert.Equal("Source Band", merged.Artist);
        Assert.Equal("Source Band", merged.AlbumArtist); // falls back via artist
        Assert.Equal("Source LP", merged.Album);
        Assert.Equal("1985", merged.Year); // normalized
        Assert.Equal("Metal", merged.Genre);
        Assert.Equal(1u, merged.Track);
        Assert.Equal(8u, merged.TrackCount);
        Assert.Equal(2u, merged.Disc);
    }

    [Fact]
    public void MergeTags_PreserveFalse_IgnoresSource()
    {
        var source = new AudioTagInfo
        {
            Title = "Source",
            Artist = "Source Artist",
            Album = "Source Album",
            Year = "1990",
            Genre = "Pop",
            Disc = 1,
            Pictures = new[] { new EmbeddedPicture { Data = new byte[] { 1, 2, 3 } } }
        };

        var merged = TaggingService.MergeTags(
            source,
            overrideTitle: "Only Title",
            overrideArtist: "Only Artist",
            overrideAlbum: "Only Album",
            preserveSource: false);

        Assert.Equal("Only Title", merged.Title);
        Assert.Equal("Only Artist", merged.Artist);
        Assert.Equal("Only Album", merged.Album);
        Assert.True(string.IsNullOrEmpty(merged.Year));
        Assert.True(string.IsNullOrEmpty(merged.Genre));
        Assert.Null(merged.Disc);
        Assert.Null(merged.Pictures);
    }

    [Fact]
    public void MergeTags_AlbumArtistFallsBackToArtist()
    {
        var merged = TaggingService.MergeTags(
            null,
            overrideArtist: "Solo",
            overrideAlbumArtist: null,
            preserveSource: true);

        Assert.Equal("Solo", merged.Artist);
        Assert.Equal("Solo", merged.AlbumArtist);
    }

    [Fact]
    public void FirstNonEmpty_Precedence()
    {
        Assert.Equal("job", TaggingService.FirstNonEmpty("job", "cue", "source", "default"));
        Assert.Equal("cue", TaggingService.FirstNonEmpty(null, "cue", "source"));
        Assert.Equal("source", TaggingService.FirstNonEmpty("", "  ", "source"));
        Assert.Equal("", TaggingService.FirstNonEmpty(null, "", "  "));
    }

    [Fact]
    public void NormalizeYear_TakesFirstFourDigits()
    {
        Assert.Equal("2021", TaggingService.NormalizeYear("2021-03-15"));
        Assert.Equal("1999", TaggingService.NormalizeYear("1999"));
        Assert.Null(TaggingService.NormalizeYear(null));
        Assert.Null(TaggingService.NormalizeYear("  "));
    }

    [Fact]
    public void ApplyTags_RoundTrip_WithTempMp3()
    {
        // Minimal valid-ish MP3 via TagLib requires a real file; create a tiny WAV-like
        // container TagLib can write ID3 to by converting: use MPEG with empty then tags.
        // TagLib can create tags on a file that already has a recognizable format.
        // Skip if we cannot create — use a pre-made approach with TagLib on FLAC header isn't easy.
        // Instead write tags onto a copy of nothing: use TagLib File.CreateTypes — not available.
        // Create a minimal AIFF/WAV using raw bytes TagLib understands.

        var dir = Path.Combine(Path.GetTempPath(), "mlm-tag-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "sample.wav");
            WriteMinimalWav(path, TimeSpan.FromMilliseconds(100));

            var tags = new AudioTagInfo
            {
                Title = "Test Title",
                Artist = "Test Artist",
                AlbumArtist = "Test AlbumArtist",
                Album = "Test Album",
                Year = "2018",
                Genre = "Test",
                Track = 4,
                TrackCount = 9,
                Disc = 1,
                DiscCount = 2
            };
            TaggingService.ApplyTags(path, tags);

            var read = TaggingService.ReadTags(path);
            Assert.Equal("Test Title", read.Title);
            Assert.Equal("Test Artist", read.Artist);
            Assert.Equal("Test AlbumArtist", read.AlbumArtist);
            Assert.Equal("Test Album", read.Album);
            Assert.Equal("2018", read.Year);
            Assert.Equal("Test", read.Genre);
            Assert.Equal(4u, read.Track);
            Assert.Equal(9u, read.TrackCount);
            Assert.Equal(1u, read.Disc);
            Assert.Equal(2u, read.DiscCount);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    /// <summary>Writes a tiny mono 8-bit PCM WAV TagLib can open.</summary>
    private static void WriteMinimalWav(string path, TimeSpan duration)
    {
        const int sampleRate = 8000;
        var samples = Math.Max(1, (int)(sampleRate * duration.TotalSeconds));
        var dataSize = samples; // 8-bit mono
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate); // byte rate
        bw.Write((short)1); // block align
        bw.Write((short)8); // bits
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);
    }
}
