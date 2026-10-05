using System.Text;
using System.Text.RegularExpressions;

namespace MusicLibraryManager.Services;

public static class FilenameSanitizer
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' })
        .Distinct()
        .ToArray();

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Sanitize(string? name, string fallback = "Unknown")
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (InvalidChars.Contains(c) || c < 32)
                sb.Append('_');
            else
                sb.Append(c);
        }

        var result = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        result = result.TrimEnd('.', ' ');
        // If nothing meaningful left (only underscores/spaces)
        if (string.IsNullOrWhiteSpace(result) || result.All(c => c == '_' || c == ' '))
            return fallback;

        var baseName = Path.GetFileNameWithoutExtension(result);
        if (Reserved.Contains(baseName))
            result = "_" + result;

        return result;
    }

    public static string ApplyPattern(string pattern, IDictionary<string, string> values)
    {
        var result = pattern;
        foreach (var kv in values)
        {
            // Skip track here if pattern uses track:00 form — handled below
            if (kv.Key.Equals("track", StringComparison.OrdinalIgnoreCase))
                continue;
            result = result.Replace("{" + kv.Key + "}", kv.Value, StringComparison.OrdinalIgnoreCase);
        }

        result = Regex.Replace(result, @"\{track:0+\}", m =>
        {
            if (!values.TryGetValue("track", out var track) &&
                !values.Keys.Any(k => k.Equals("track", StringComparison.OrdinalIgnoreCase)))
                return "00";
            values.TryGetValue("track", out track);
            if (track == null)
            {
                var key = values.Keys.First(k => k.Equals("track", StringComparison.OrdinalIgnoreCase));
                track = values[key];
            }
            if (!int.TryParse(track, out var n))
                return "00";
            var zeros = m.Value.Count(c => c == '0');
            return n.ToString(new string('0', Math.Max(zeros, 1)));
        }, RegexOptions.IgnoreCase);

        // Plain {track}
        if (values.TryGetValue("track", out var t) ||
            values.Keys.Any(k => k.Equals("track", StringComparison.OrdinalIgnoreCase)))
        {
            if (t == null)
            {
                var key = values.Keys.First(k => k.Equals("track", StringComparison.OrdinalIgnoreCase));
                t = values[key];
            }
            result = Regex.Replace(result, @"\{track\}", t, RegexOptions.IgnoreCase);
        }

        return result;
    }
}
