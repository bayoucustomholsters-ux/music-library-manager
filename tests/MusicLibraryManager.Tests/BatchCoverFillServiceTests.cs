using MusicLibraryManager.Models;
using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class BatchCoverFillServiceTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mlm-batch-cover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData("cover.jpg")]
    [InlineData("Cover.JPG")]
    [InlineData("FOLDER.png")]
    [InlineData("front.jpeg")]
    [InlineData("Album.PNG")]
    public void HasExistingCover_DetectsConventionalNames(string fileName)
    {
        var dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, fileName), new byte[] { 1, 2, 3 });
            Assert.True(BatchCoverFillService.HasExistingCover(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("artwork.jpg")]
    [InlineData("scan.png")]
    [InlineData("cover.gif")]
    [InlineData("readme.txt")]
    public void HasExistingCover_IgnoresNonMatching(string fileName)
    {
        var dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, fileName), new byte[] { 1, 2, 3 });
            Assert.False(BatchCoverFillService.HasExistingCover(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void HasExistingCover_FalseWhenEmptyOrMissing()
    {
        var dir = TempDir();
        try
        {
            Assert.False(BatchCoverFillService.HasExistingCover(dir));
            Assert.False(BatchCoverFillService.HasExistingCover(Path.Combine(dir, "nope")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanFolderName_TrimsAndUnscores()
    {
        Assert.Equal("Siamese Dream", BatchCoverFillService.CleanFolderName("  Siamese_Dream  "));
        Assert.Equal("Adore", BatchCoverFillService.CleanFolderName("Adore"));
        Assert.Equal("", BatchCoverFillService.CleanFolderName("   "));
    }

    [Fact]
    public void ResolveArtistAlbum_UsesFolderNamesWhenNoAudio()
    {
        var root = TempDir();
        try
        {
            var artist = Path.Combine(root, "The Smashing Pumpkins");
            var album = Path.Combine(artist, "Siamese_Dream");
            Directory.CreateDirectory(album);

            var (a, b) = BatchCoverFillService.ResolveArtistAlbum(artist, album);
            Assert.Equal("The Smashing Pumpkins", a);
            Assert.Equal("Siamese Dream", b);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetImmediateSubdirectories_ReturnsSortedChildrenOnly()
    {
        var root = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Gish"));
            Directory.CreateDirectory(Path.Combine(root, "Adore"));
            Directory.CreateDirectory(Path.Combine(root, "Gish", "nested"));
            File.WriteAllText(Path.Combine(root, "readme.txt"), "x");

            var kids = BatchCoverFillService.GetImmediateSubdirectories(root);
            Assert.Equal(2, kids.Count);
            Assert.Equal("Adore", Path.GetFileName(kids[0]));
            Assert.Equal("Gish", Path.GetFileName(kids[1]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectBestCandidate_PrefersHighestScore()
    {
        var list = new List<MusicBrainzReleaseCandidate>
        {
            new() { ReleaseId = "low", Album = "A", Score = 40 },
            new() { ReleaseId = "high", Album = "B", Score = 98 },
            new() { ReleaseId = "mid", Album = "C", Score = 70 }
        };
        var best = BatchCoverFillService.SelectBestCandidate(list);
        Assert.NotNull(best);
        Assert.Equal("high", best!.ReleaseId);
    }

    [Fact]
    public void SelectBestCandidate_FirstWhenNoScores()
    {
        var list = new List<MusicBrainzReleaseCandidate>
        {
            new() { ReleaseId = "first", Album = "A" },
            new() { ReleaseId = "second", Album = "B" }
        };
        var best = BatchCoverFillService.SelectBestCandidate(list);
        Assert.Equal("first", best!.ReleaseId);
    }

    [Fact]
    public void SelectBestCandidate_NullWhenEmpty()
    {
        Assert.Null(BatchCoverFillService.SelectBestCandidate(Array.Empty<MusicBrainzReleaseCandidate>()));
    }
}
