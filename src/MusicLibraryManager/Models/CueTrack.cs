namespace MusicLibraryManager.Models;

public sealed class CueTrack
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Performer { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Index00 { get; set; }
}
