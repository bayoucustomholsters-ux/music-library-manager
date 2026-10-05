namespace MusicLibraryManager.Models;

public sealed class AcoustIdLookupResult
{
    public string Status { get; set; } = "";
    public IReadOnlyList<AcoustIdMatch> Results { get; set; } = Array.Empty<AcoustIdMatch>();
}

public sealed class AcoustIdMatch
{
    public string Id { get; set; } = "";
    public double Score { get; set; }
    public IReadOnlyList<AcoustIdRecording> Recordings { get; set; } = Array.Empty<AcoustIdRecording>();
}

public sealed class AcoustIdRecording
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public IReadOnlyList<AcoustIdReleaseRef> Releases { get; set; } = Array.Empty<AcoustIdReleaseRef>();
}

public sealed class AcoustIdReleaseRef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Date { get; set; }
    public string? Country { get; set; }
    public int TrackCount { get; set; }
}
