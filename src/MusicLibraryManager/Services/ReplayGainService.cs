using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Computes track ReplayGain via ffmpeg loudnorm scan and writes REPLAYGAIN_* tags via TagLib.
/// Failures are non-fatal — callers should catch and log.
/// </summary>
public sealed class ReplayGainService
{
    /// <summary>ReplayGain reference loudness (LUFS), classic RG ≈ -18.</summary>
    public const double ReferenceLufs = -18.0;

    private readonly string _ffmpegPath;
    private readonly Action<string>? _log;

    public ReplayGainService(string ffmpegPath, Action<string>? log = null)
    {
        _ffmpegPath = ffmpegPath;
        _log = log;
    }

    public async Task<ReplayGainInfo?> AnalyzeTrackAsync(string audioPath, CancellationToken ct = default)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("Audio not found.", audioPath);

        var args =
            $"-hide_banner -i \"{audioPath}\" -af loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json -f null -";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg for ReplayGain.");

        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        _ = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        // loudnorm JSON is printed on stderr
        var info = ParseLoudnormJson(stderr);
        if (info == null)
            Log($"ReplayGain: could not parse loudnorm output for {Path.GetFileName(audioPath)}");
        return info;
    }

    public static ReplayGainInfo? ParseLoudnormJson(string ffmpegStderrOrJson)
    {
        var json = ExtractJsonObject(ffmpegStderrOrJson);
        if (json == null) return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!TryGetDouble(root, "input_i", out var inputI))
            return null;

        TryGetDouble(root, "input_tp", out var inputTp);

        // Track gain relative to classic ReplayGain reference (-18 LUFS)
        var gainDb = ReferenceLufs - inputI;
        // Peak as linear amplitude from dBTP: peak = 10^(tp/20)
        var peakLinear = Math.Pow(10.0, inputTp / 20.0);
        if (double.IsNaN(peakLinear) || double.IsInfinity(peakLinear) || peakLinear <= 0)
            peakLinear = 1.0;

        return new ReplayGainInfo
        {
            TrackGainDb = Math.Round(gainDb, 2),
            TrackPeak = Math.Round(peakLinear, 6)
        };
    }

    public static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Prefer last JSON object in the stream (loudnorm prints after processing)
        var matches = Regex.Matches(text, @"\{[^{}]*\}", RegexOptions.Singleline);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var candidate = matches[i].Value;
            if (candidate.Contains("input_i", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        // Nested braces fallback
        var start = text.LastIndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var slice = text[start..(end + 1)];
            if (slice.Contains("input_i", StringComparison.OrdinalIgnoreCase))
                return slice;
        }

        return null;
    }

    /// <summary>
    /// Analyze and write ReplayGain tags. Returns false on failure (does not throw).
    /// </summary>
    public async Task<bool> ApplyToFileAsync(string audioPath, CancellationToken ct = default)
    {
        try
        {
            var info = await AnalyzeTrackAsync(audioPath, ct).ConfigureAwait(false);
            if (info == null) return false;
            TaggingService.ApplyReplayGainTags(audioPath, info);
            Log($"ReplayGain: {Path.GetFileName(audioPath)} → {info.TrackGainTag} (peak {info.TrackPeakTag})");
            return true;
        }
        catch (Exception ex)
        {
            Log($"ReplayGain warning: {Path.GetFileName(audioPath)}: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number)
        {
            value = el.GetDouble();
            return true;
        }
        if (el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        return false;
    }

    private void Log(string msg) => _log?.Invoke(msg);
}
