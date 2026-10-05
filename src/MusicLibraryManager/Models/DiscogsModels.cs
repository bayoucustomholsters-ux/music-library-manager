namespace MusicLibraryManager.Models;

public sealed class DiscogsReleaseCandidate
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public string? Style { get; set; }
    public string? Country { get; set; }
    public string? Format { get; set; }
    public string? Label { get; set; }
    public string? ThumbUrl { get; set; }
    public string? ResourceUrl { get; set; }
    public List<DiscogsTrackInfo> Tracks { get; set; } = new();

    public string DisplayLabel
    {
        get
        {
            var year = string.IsNullOrWhiteSpace(Year) ? "????" : Year;
            var format = string.IsNullOrWhiteSpace(Format) ? "" : $" · {Format}";
            var country = string.IsNullOrWhiteSpace(Country) ? "" : $" [{Country}]";
            var genre = string.IsNullOrWhiteSpace(Genre) ? "" : $" · {Genre}";
            return $"{Artist} — {Album} ({year}){country}{format}{genre}";
        }
    }
}

public sealed class DiscogsTrackInfo
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Duration { get; set; }
    public string? Position { get; set; }
}
