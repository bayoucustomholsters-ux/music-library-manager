namespace MusicLibraryManager.Services;

/// <summary>
/// Parses a plain-text track list (one title per line, optional "01. Title" / "01 - Title").
/// </summary>
public static class TrackListParser
{
    public static List<string> Parse(string path)
    {
        var lines = System.IO.File.ReadAllLines(path);
        var titles = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith('#') || line.StartsWith("//"))
                continue;

            // Strip leading track numbers
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                line, @"^\s*\d+\s*[\.\-\:\)\]]\s*", "");
            if (!string.IsNullOrWhiteSpace(cleaned))
                titles.Add(cleaned.Trim());
        }
        return titles;
    }
}
