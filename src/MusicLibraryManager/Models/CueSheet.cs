namespace MusicLibraryManager.Models;

public sealed class CueSheet
{
    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Performer { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public string? Date { get; set; }
    public string? Comment { get; set; }
    public List<CueTrack> Tracks { get; set; } = new();
    public string? SourcePath { get; set; }
}
