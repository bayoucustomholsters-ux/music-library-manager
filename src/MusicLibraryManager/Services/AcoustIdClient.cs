using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// AcoustID lookup: run Chromaprint fpcalc, then query api.acoustid.org.
/// API key must be supplied by the user (never hardcoded).
/// </summary>
public sealed class AcoustIdClient : IDisposable
{
    public const string ApiBase = "https://api.acoustid.org/v2/lookup";
    public const string UserAgent = "MusicLibraryManager/5.1.0 ( https://github.com/bayoucustomholsters/music-library-manager )";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(1100);

    public AcoustIdClient(Action<string>? log = null, HttpClient? httpClient = null)
    {
        _log = log;
        if (httpClient != null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _ownsHttp = true;
        }
    }

    public async Task<(int DurationSec, string Fingerprint)> FingerprintAsync(
        string audioPath,
        string fpcalcPath,
        int lengthSeconds = 120,
        CancellationToken ct = default)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("Audio file not found.", audioPath);
        if (!File.Exists(fpcalcPath))
            throw new FileNotFoundException("fpcalc not found.", fpcalcPath);

        var args = $"-json -length {lengthSeconds} \"{audioPath}\"";
        Log($"fpcalc: {Path.GetFileName(audioPath)} (first {lengthSeconds}s)");

        var psi = new ProcessStartInfo
        {
            FileName = fpcalcPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start fpcalc.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"fpcalc failed (exit {proc.ExitCode}): {Truncate(stderr, 300)}");

        return ParseFpcalcJson(stdout);
    }

    public static (int DurationSec, string Fingerprint) ParseFpcalcJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var duration = 0;
        if (root.TryGetProperty("duration", out var d))
        {
            if (d.ValueKind == JsonValueKind.Number)
                duration = (int)Math.Round(d.GetDouble());
            else if (d.ValueKind == JsonValueKind.String && int.TryParse(d.GetString(), out var di))
                duration = di;
        }

        var fingerprint = root.TryGetProperty("fingerprint", out var fp) ? fp.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new InvalidOperationException("fpcalc produced no fingerprint.");
        if (duration <= 0)
            throw new InvalidOperationException("fpcalc produced invalid duration.");

        return (duration, fingerprint);
    }

    public async Task<AcoustIdLookupResult> LookupAsync(
        string apiKey,
        int durationSec,
        string fingerprint,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "AcoustID API key not set. Get a free key at https://acoustid.org/api and enter it in Settings.");

        var url =
            $"{ApiBase}?client={Uri.EscapeDataString(apiKey.Trim())}" +
            $"&meta=recordings+releases+releasegroups+tracks" +
            $"&duration={durationSec}" +
            $"&fingerprint={Uri.EscapeDataString(fingerprint)}";

        Log("AcoustID lookup...");
        await EnforceRateLimitAsync(ct).ConfigureAwait(false);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"AcoustID HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");

        return ParseLookupResponse(body);
    }

    public static AcoustIdLookupResult ParseLookupResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
        var result = new AcoustIdLookupResult { Status = status };

        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return result;

        var list = new List<AcoustIdMatch>();
        foreach (var r in results.EnumerateArray())
        {
            var match = new AcoustIdMatch
            {
                Id = r.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Score = r.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number
                    ? sc.GetDouble() : 0
            };

            if (r.TryGetProperty("recordings", out var recordings) && recordings.ValueKind == JsonValueKind.Array)
            {
                var recs = new List<AcoustIdRecording>();
                foreach (var rec in recordings.EnumerateArray())
                {
                    var recording = new AcoustIdRecording
                    {
                        Id = rec.TryGetProperty("id", out var rid) ? rid.GetString() ?? "" : "",
                        Title = rec.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                        Artist = ExtractArtists(rec)
                    };

                    if (rec.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array)
                    {
                        var rels = new List<AcoustIdReleaseRef>();
                        foreach (var rel in releases.EnumerateArray())
                        {
                            var trackCount = 0;
                            if (rel.TryGetProperty("track_count", out var tc) && tc.TryGetInt32(out var n))
                                trackCount = n;
                            else if (rel.TryGetProperty("medium_count", out _) &&
                                     rel.TryGetProperty("track_count", out var tc2) && tc2.TryGetInt32(out var n2))
                                trackCount = n2;

                            if (trackCount == 0 && rel.TryGetProperty("mediums", out var mediums)
                                && mediums.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var m in mediums.EnumerateArray())
                                {
                                    if (m.TryGetProperty("track_count", out var mtc) && mtc.TryGetInt32(out var mn))
                                        trackCount += mn;
                                }
                            }

                            rels.Add(new AcoustIdReleaseRef
                            {
                                Id = rel.TryGetProperty("id", out var relId) ? relId.GetString() ?? "" : "",
                                Title = rel.TryGetProperty("title", out var rt) ? rt.GetString() ?? "" : "",
                                Date = rel.TryGetProperty("date", out var date) ? date.GetString() : null,
                                Country = rel.TryGetProperty("country", out var co) ? co.GetString() : null,
                                TrackCount = trackCount
                            });
                        }
                        recording.Releases = rels;
                    }

                    recs.Add(recording);
                }
                match.Recordings = recs;
            }

            list.Add(match);
        }

        result.Results = list;
        return result;
    }

    /// <summary>
    /// Maps AcoustID matches into MusicBrainz release candidates (deduped by release id).
    /// </summary>
    public static IReadOnlyList<MusicBrainzReleaseCandidate> ToMusicBrainzCandidates(AcoustIdLookupResult lookup)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<MusicBrainzReleaseCandidate>();

        foreach (var match in lookup.Results.OrderByDescending(m => m.Score))
        {
            foreach (var rec in match.Recordings)
            {
                if (rec.Releases.Count == 0)
                {
                    // Recording without releases — still offer a synthetic candidate keyed by recording
                    // so the UI can show something; caller should prefer real release ids.
                    continue;
                }

                foreach (var rel in rec.Releases)
                {
                    if (string.IsNullOrWhiteSpace(rel.Id) || !seen.Add(rel.Id))
                        continue;

                    list.Add(new MusicBrainzReleaseCandidate
                    {
                        ReleaseId = rel.Id,
                        Artist = rec.Artist,
                        Album = string.IsNullOrWhiteSpace(rel.Title) ? rec.Title : rel.Title,
                        Year = MusicBrainzClient.ExtractYear(rel.Date),
                        TrackCount = rel.TrackCount,
                        Country = rel.Country,
                        Disambiguation = $"AcoustID score {match.Score:0.00}"
                    });
                }
            }
        }

        return list;
    }

    private static string ExtractArtists(JsonElement rec)
    {
        if (!rec.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new StringBuilder();
        foreach (var a in artists.EnumerateArray())
        {
            if (a.TryGetProperty("name", out var name))
                sb.Append(name.GetString());
            if (a.TryGetProperty("joinphrase", out var jp))
                sb.Append(jp.GetString());
        }
        return sb.ToString().Trim();
    }

    private async Task EnforceRateLimitAsync(CancellationToken ct)
    {
        await _rateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (elapsed < _minInterval)
                await Task.Delay(_minInterval - elapsed, ct).ConfigureAwait(false);
            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _rateGate.Release();
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");

    private void Log(string msg) => _log?.Invoke(msg);

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _rateGate.Dispose();
    }
}
