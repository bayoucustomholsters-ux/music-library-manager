using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

public static class CueParser
{
    private static readonly Regex FileRegex = new(
        @"^\s*FILE\s+""(?<name>.+?)""\s*(?<type>\S+)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrackRegex = new(
        @"^\s*TRACK\s+(?<num>\d+)\s+(?<type>\S+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IndexRegex = new(
        @"^\s*INDEX\s+(?<num>\d+)\s+(?<mm>\d+):(?<ss>\d+):(?<ff>\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuotedRegex = new(
        @"^\s*(?<key>TITLE|PERFORMER|SONGWRITER)\s+""(?<val>.*)""\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RemRegex = new(
        @"^\s*REM\s+(?<key>\S+)\s+(?<val>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CueSheet ParseFile(string path)
    {
        var encoding = DetectEncoding(path);
        var lines = File.ReadAllLines(path, encoding);
        var sheet = ParseLines(lines);
        sheet.SourcePath = path;
        return sheet;
    }

    public static CueSheet ParseLines(IEnumerable<string> lines)
    {
        var sheet = new CueSheet();
        CueTrack? current = null;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fileMatch = FileRegex.Match(line);
            if (fileMatch.Success)
            {
                sheet.FileName = fileMatch.Groups["name"].Value;
                sheet.FileType = fileMatch.Groups["type"].Success
                    ? fileMatch.Groups["type"].Value
                    : "WAVE";
                continue;
            }

            var trackMatch = TrackRegex.Match(line);
            if (trackMatch.Success)
            {
                current = new CueTrack
                {
                    Number = int.Parse(trackMatch.Groups["num"].Value, CultureInfo.InvariantCulture)
                };
                sheet.Tracks.Add(current);
                continue;
            }

            var quoted = QuotedRegex.Match(line);
            if (quoted.Success)
            {
                var key = quoted.Groups["key"].Value.ToUpperInvariant();
                var val = Unescape(quoted.Groups["val"].Value);
                if (current == null)
                {
                    switch (key)
                    {
                        case "TITLE": sheet.Title = val; break;
                        case "PERFORMER": sheet.Performer = val; break;
                    }
                }
                else
                {
                    switch (key)
                    {
                        case "TITLE": current.Title = val; break;
                        case "PERFORMER": current.Performer = val; break;
                    }
                }
                continue;
            }

            var indexMatch = IndexRegex.Match(line);
            if (indexMatch.Success && current != null)
            {
                var indexNum = int.Parse(indexMatch.Groups["num"].Value, CultureInfo.InvariantCulture);
                var time = FramesToTimeSpan(
                    int.Parse(indexMatch.Groups["mm"].Value, CultureInfo.InvariantCulture),
                    int.Parse(indexMatch.Groups["ss"].Value, CultureInfo.InvariantCulture),
                    int.Parse(indexMatch.Groups["ff"].Value, CultureInfo.InvariantCulture));

                if (indexNum == 1)
                    current.StartTime = time;
                else if (indexNum == 0)
                    current.Index00 = time.ToString();
                continue;
            }

            var rem = RemRegex.Match(line);
            if (rem.Success)
            {
                var key = rem.Groups["key"].Value.ToUpperInvariant();
                var val = rem.Groups["val"].Value.Trim().Trim('"');
                switch (key)
                {
                    case "DATE":
                    case "YEAR":
                        sheet.Date = val;
                        break;
                    case "GENRE":
                        sheet.Genre = val;
                        break;
                    case "COMMENT":
                        sheet.Comment = val;
                        break;
                }
            }
        }

        // Compute end times from next track start
        for (var i = 0; i < sheet.Tracks.Count; i++)
        {
            if (i + 1 < sheet.Tracks.Count)
                sheet.Tracks[i].EndTime = sheet.Tracks[i + 1].StartTime;
        }

        return sheet;
    }

    public static TimeSpan FramesToTimeSpan(int minutes, int seconds, int frames)
    {
        // CDDA: 75 frames per second
        var totalSeconds = minutes * 60.0 + seconds + frames / 75.0;
        return TimeSpan.FromSeconds(totalSeconds);
    }

    private static string Unescape(string s) => s.Replace("\\\"", "\"");

    private static Encoding DetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // Prefer UTF-8; fall back to Latin-1 for classic CUE sheets
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            utf8.GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch
        {
            return Encoding.GetEncoding(28591); // Latin-1
        }
    }
}
