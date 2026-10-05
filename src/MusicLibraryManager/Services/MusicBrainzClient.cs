using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// MusicBrainz Web Service client (no API key). Rate-limited ~1 req/sec.
/// Cover Art Archive downloads use the release MBID.
/// </summary>
public sealed class MusicBrainzClient : IDisposable
{
    public const string UserAgent = "MusicLibraryManager/5.1.0 ( https://github.com/bayoucustomholsters/music-library-manager )";
    public const string BaseUrl = "https://musicbrainz.org/ws/2/";
    public const string CoverArtBaseUrl = "https://coverartarchive.org/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(1100);
    private readonly Action<string>? _log;
    private bool _ownsHttp;

    public MusicBrainzClient(Action<string>? log = null, HttpClient? httpClient = null)
    {
        _log = log;
        if (httpClient != null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _ownsHttp = true;
        }
    }

    public async Task<IReadOnlyList<MusicBrainzReleaseCandidate>> SearchReleasesAsync(
        string? artist,
        string? album,
        CancellationToken ct = default)
    {
        artist = artist?.Trim() ?? "";
        album = album?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(album))
            return Array.Empty<MusicBrainzReleaseCandidate>();

        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(artist))
            queryParts.Add($"artist:\"{EscapeLucene(artist)}\"");
        if (!string.IsNullOrWhiteSpace(album))
            queryParts.Add($"release:\"{EscapeLucene(album)}\"");

        var query = string.Join(" AND ", queryParts);
        var url = $"{BaseUrl}release?query={Uri.EscapeDataString(query)}&fmt=json&limit=10";

        Log($"MusicBrainz search: {artist} / {album}");
        var json = await GetStringAsync(url, ct).ConfigureAwait(false);
        return ParseSearchResults(json);
    }

    /// <summary>
    /// Heuristic: parse "Artist - Album" from a filename stem.
    /// </summary>
    public static (string? Artist, string? Album) GuessFromFilename(string audioPath)
    {
        // Normalize Windows separators so this works when unit-tested on Linux
        var normalized = audioPath.Replace('\\', '/');
        var stem = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrWhiteSpace(stem))
            return (null, null);

        // Common patterns: "Artist - Album", "Artist – Album", "Artist_Album"
        var parts = Regex.Split(stem, @"\s+[-–—]\s+", RegexOptions.Compiled);
        if (parts.Length >= 2)
            return (parts[0].Trim(), string.Join(" - ", parts.Skip(1)).Trim());

        return (null, stem.Trim());
    }

    public async Task<MusicBrainzReleaseCandidate?> GetReleaseDetailsAsync(string releaseId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
            return null;

        var url = $"{BaseUrl}release/{releaseId}?fmt=json&inc=recordings+artist-credits+tags+release-groups";
        Log($"MusicBrainz release detail: {releaseId}");
        var json = await GetStringAsync(url, ct).ConfigureAwait(false);
        return ParseReleaseDetail(json);
    }

    /// <summary>
    /// Lookup releases by MusicBrainz DiscID. Returns empty on 404 (unknown disc).
    /// </summary>
    public async Task<IReadOnlyList<MusicBrainzReleaseCandidate>> LookupDiscIdAsync(string discId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(discId))
            return Array.Empty<MusicBrainzReleaseCandidate>();

        var url = $"{BaseUrl}discid/{Uri.EscapeDataString(discId)}?fmt=json&inc=recordings+artist-credits+tags+release-groups";
        Log($"MusicBrainz DiscID lookup: {discId}");
        var json = await GetStringOrNullAsync(url, ct).ConfigureAwait(false);
        if (json == null)
        {
            Log("MusicBrainz: no release for this DiscID.");
            return Array.Empty<MusicBrainzReleaseCandidate>();
        }
        return ParseDiscIdResults(json);
    }

    /// <summary>
    /// Fuzzy TOC lookup: <c>first last leadout off1 off2 ...</c> (spaces or pluses).
    /// </summary>
    public async Task<IReadOnlyList<MusicBrainzReleaseCandidate>> LookupByTocAsync(string tocQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tocQuery))
            return Array.Empty<MusicBrainzReleaseCandidate>();

        var toc = tocQuery.Trim().Replace(' ', '+');
        var url = $"{BaseUrl}discid/-?toc={toc}&fmt=json&inc=recordings+artist-credits+tags+release-groups";
        Log($"MusicBrainz TOC lookup: {tocQuery.Trim()}");
        var json = await GetStringOrNullAsync(url, ct).ConfigureAwait(false);
        if (json == null)
        {
            Log("MusicBrainz: no release for this TOC.");
            return Array.Empty<MusicBrainzReleaseCandidate>();
        }
        return ParseDiscIdResults(json);
    }

    public static IReadOnlyList<MusicBrainzReleaseCandidate> ParseDiscIdResults(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            return Array.Empty<MusicBrainzReleaseCandidate>();

        var list = new List<MusicBrainzReleaseCandidate>();
        foreach (var rel in releases.EnumerateArray())
        {
            var candidate = ParseReleaseSummary(rel, includeTracks: true);
            if (candidate != null)
                list.Add(candidate);
        }
        return list;
    }

    public async Task<string?> DownloadFrontCoverAsync(string releaseId, string destinationJpgPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationJpgPath)!);
        // Prefer 500px front; falls back via Cover Art Archive redirects
        var url = $"{CoverArtBaseUrl}release/{releaseId}/front-500";
        Log($"Cover Art Archive: {releaseId}");

        await EnforceRateLimitAsync(ct).ConfigureAwait(false);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Log("No cover art found for this release.");
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fs = File.Create(destinationJpgPath);
        await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
        Log($"Cover saved: {destinationJpgPath}");
        return destinationJpgPath;
    }

    public static IReadOnlyList<MusicBrainzReleaseCandidate> ParseSearchResults(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            return Array.Empty<MusicBrainzReleaseCandidate>();

        var list = new List<MusicBrainzReleaseCandidate>();
        foreach (var rel in releases.EnumerateArray())
        {
            var candidate = ParseReleaseSummary(rel);
            if (candidate != null)
                list.Add(candidate);
        }
        return list;
    }

    public static MusicBrainzReleaseCandidate? ParseReleaseDetail(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseReleaseSummary(doc.RootElement, includeTracks: true);
    }

    public static MusicBrainzReleaseCandidate? ParseReleaseSummary(JsonElement rel, bool includeTracks = false)
    {
        if (!rel.TryGetProperty("id", out var idEl))
            return null;

        var id = idEl.GetString() ?? "";
        if (string.IsNullOrEmpty(id))
            return null;

        var title = rel.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var artist = ExtractArtistCredit(rel);
        var date = rel.TryGetProperty("date", out var d) ? d.GetString() : null;
        var year = ExtractYear(date);
        if (string.IsNullOrEmpty(year) && rel.TryGetProperty("release-group", out var rg)
            && rg.TryGetProperty("first-release-date", out var frd))
            year = ExtractYear(frd.GetString());

        var country = rel.TryGetProperty("country", out var c) ? c.GetString() : null;
        var status = rel.TryGetProperty("status", out var s) ? s.GetString() : null;
        var disambig = rel.TryGetProperty("disambiguation", out var di) ? di.GetString() : null;

        var trackCount = 0;
        if (rel.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in media.EnumerateArray())
            {
                if (m.TryGetProperty("track-count", out var tc) && tc.TryGetInt32(out var n))
                    trackCount += n;
                else if (m.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
                    trackCount += tracks.GetArrayLength();
            }
        }

        var genre = ExtractGenre(rel);
        int? score = null;
        if (rel.TryGetProperty("score", out var scoreEl))
        {
            if (scoreEl.ValueKind == JsonValueKind.Number && scoreEl.TryGetInt32(out var sc))
                score = sc;
            else if (scoreEl.ValueKind == JsonValueKind.String && int.TryParse(scoreEl.GetString(), out var scs))
                score = scs;
        }

        var candidate = new MusicBrainzReleaseCandidate
        {
            ReleaseId = id,
            Artist = artist,
            Album = title,
            Year = year,
            Genre = genre,
            TrackCount = trackCount,
            Disambiguation = disambig,
            Country = country,
            Status = status,
            Score = score
        };

        if (includeTracks)
            candidate.Tracks = ExtractTracks(rel);

        if (candidate.TrackCount == 0 && candidate.Tracks.Count > 0)
            candidate.TrackCount = candidate.Tracks.Count;

        return candidate;
    }

    public static List<MusicBrainzTrackInfo> ExtractTracks(JsonElement release)
    {
        var result = new List<MusicBrainzTrackInfo>();
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
            return result;

        var globalNum = 1;
        foreach (var m in media.EnumerateArray())
        {
            if (!m.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var tr in tracks.EnumerateArray())
            {
                var title = "";
                if (tr.TryGetProperty("title", out var tt))
                    title = tt.GetString() ?? "";
                else if (tr.TryGetProperty("recording", out var rec) && rec.TryGetProperty("title", out var rt))
                    title = rt.GetString() ?? "";

                var number = globalNum;
                if (tr.TryGetProperty("number", out var numEl))
                {
                    var numStr = numEl.ValueKind == JsonValueKind.String
                        ? numEl.GetString()
                        : numEl.ToString();
                    if (int.TryParse(numStr, out var parsed) && parsed > 0)
                        number = parsed;
                }
                else if (tr.TryGetProperty("position", out var pos) && pos.TryGetInt32(out var p) && p > 0)
                {
                    number = p;
                }

                int? lengthMs = null;
                if (tr.TryGetProperty("length", out var len) && len.TryGetInt32(out var ms))
                    lengthMs = ms;

                result.Add(new MusicBrainzTrackInfo
                {
                    Number = number,
                    Title = string.IsNullOrWhiteSpace(title) ? $"Track {number:00}" : title,
                    LengthMs = lengthMs
                });
                globalNum++;
            }
        }

        // Re-number sequentially if media had per-disc numbers that collide
        if (result.Count > 1 && result.Select(t => t.Number).Distinct().Count() < result.Count)
        {
            for (var i = 0; i < result.Count; i++)
                result[i].Number = i + 1;
        }

        return result;
    }

    public static string ExtractArtistCredit(JsonElement rel)
    {
        if (!rel.TryGetProperty("artist-credit", out var ac) || ac.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new System.Text.StringBuilder();
        foreach (var credit in ac.EnumerateArray())
        {
            if (credit.TryGetProperty("name", out var name))
                sb.Append(name.GetString());
            else if (credit.TryGetProperty("artist", out var artist)
                     && artist.TryGetProperty("name", out var an))
                sb.Append(an.GetString());

            if (credit.TryGetProperty("joinphrase", out var jp))
                sb.Append(jp.GetString());
        }
        return sb.ToString().Trim();
    }

    public static string? ExtractYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var digits = new string(date.Where(char.IsDigit).Take(4).ToArray());
        return digits.Length == 4 ? digits : null;
    }

    public static string? ExtractGenre(JsonElement rel)
    {
        // Prefer release tags, then release-group tags
        string? fromTags(JsonElement el)
        {
            if (!el.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
                return null;
            string? best = null;
            var bestCount = -1;
            foreach (var tag in tags.EnumerateArray())
            {
                var name = tag.TryGetProperty("name", out var n) ? n.GetString() : null;
                var count = tag.TryGetProperty("count", out var c) && c.TryGetInt32(out var v) ? v : 0;
                if (!string.IsNullOrWhiteSpace(name) && count >= bestCount)
                {
                    best = name;
                    bestCount = count;
                }
            }
            return best;
        }

        return fromTags(rel)
               ?? (rel.TryGetProperty("release-group", out var rg) ? fromTags(rg) : null);
    }

    private static string EscapeLucene(string value)
    {
        // Escape Lucene special characters used in field queries
        return Regex.Replace(value, @"([+\-&|!(){}\[\]^""~*?:\\/])", "\\$1");
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        await EnforceRateLimitAsync(ct).ConfigureAwait(false);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"MusicBrainz HTTP {(int)response.StatusCode}: {response.ReasonPhrase}. {Truncate(body, 200)}");
        }
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<string?> GetStringOrNullAsync(string url, CancellationToken ct)
    {
        await EnforceRateLimitAsync(ct).ConfigureAwait(false);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"MusicBrainz HTTP {(int)response.StatusCode}: {response.ReasonPhrase}. {Truncate(body, 200)}");
        }
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
        s.Length <= max ? s : s[..max] + "...";

    private void Log(string msg) => _log?.Invoke(msg);

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
        _rateGate.Dispose();
    }
}
