using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Discogs API client. Requires a personal access token and a descriptive User-Agent.
/// Rate-limited politely (~1 req/sec).
/// </summary>
public sealed class DiscogsClient : IDisposable
{
    public const string ApiBase = "https://api.discogs.com/";
    public const string DefaultUserAgent = "MusicLibraryManager/5.1.0 +https://github.com/bayoucustomholsters/music-library-manager";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(1100);

    public DiscogsClient(string? userAgent = null, Action<string>? log = null, HttpClient? httpClient = null)
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
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent.Trim());
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _ownsHttp = true;
        }
    }

    public async Task<IReadOnlyList<DiscogsReleaseCandidate>> SearchReleasesAsync(
        string token,
        string? artist,
        string? album,
        CancellationToken ct = default)
    {
        EnsureToken(token);
        artist = artist?.Trim() ?? "";
        album = album?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(album))
            return Array.Empty<DiscogsReleaseCandidate>();

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(artist)) query.Add($"artist={Uri.EscapeDataString(artist)}");
        if (!string.IsNullOrWhiteSpace(album)) query.Add($"release_title={Uri.EscapeDataString(album)}");
        query.Add("type=release");
        query.Add("per_page=15");

        var url = $"{ApiBase}database/search?{string.Join("&", query)}";
        Log($"Discogs search: {artist} / {album}");
        var json = await GetStringAsync(url, token, ct).ConfigureAwait(false);
        return ParseSearchResults(json);
    }

    public async Task<DiscogsReleaseCandidate?> GetReleaseAsync(
        string token,
        long releaseId,
        CancellationToken ct = default)
    {
        EnsureToken(token);
        var url = $"{ApiBase}releases/{releaseId}";
        Log($"Discogs release: {releaseId}");
        var json = await GetStringAsync(url, token, ct).ConfigureAwait(false);
        return ParseReleaseDetail(json);
    }

    public static IReadOnlyList<DiscogsReleaseCandidate> ParseSearchResults(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return Array.Empty<DiscogsReleaseCandidate>();

        var list = new List<DiscogsReleaseCandidate>();
        foreach (var r in results.EnumerateArray())
        {
            var c = ParseSearchHit(r);
            if (c != null) list.Add(c);
        }
        return list;
    }

    public static DiscogsReleaseCandidate? ParseReleaseDetail(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseReleaseDetailElement(doc.RootElement);
    }

    public static DiscogsReleaseCandidate? ParseSearchHit(JsonElement r)
    {
        if (!r.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
            return null;

        var title = r.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var (artist, album) = SplitDiscogsTitle(title);

        string? year = null;
        if (r.TryGetProperty("year", out var y))
        {
            year = y.ValueKind == JsonValueKind.Number ? y.GetInt32().ToString() : y.GetString();
            year = MusicBrainzClient.ExtractYear(year);
        }

        var genre = FirstArrayString(r, "genre");
        var style = FirstArrayString(r, "style");
        var country = r.TryGetProperty("country", out var c) ? c.GetString() : null;
        var format = FirstArrayString(r, "format");
        var label = FirstArrayString(r, "label");
        var thumb = r.TryGetProperty("thumb", out var th) ? th.GetString() : null;
        var resource = r.TryGetProperty("resource_url", out var ru) ? ru.GetString() : null;

        return new DiscogsReleaseCandidate
        {
            Id = id,
            Title = title,
            Artist = artist,
            Album = album,
            Year = year,
            Genre = genre ?? style,
            Style = style,
            Country = country,
            Format = format,
            Label = label,
            ThumbUrl = thumb,
            ResourceUrl = resource
        };
    }

    public static DiscogsReleaseCandidate? ParseReleaseDetailElement(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
            return null;

        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var artist = ExtractArtists(root);
        if (string.IsNullOrWhiteSpace(artist))
        {
            var split = SplitDiscogsTitle(title);
            artist = split.Artist;
            if (string.IsNullOrWhiteSpace(title) || title.Contains(" - "))
                title = split.Album;
        }

        string? year = null;
        if (root.TryGetProperty("year", out var y))
        {
            year = y.ValueKind == JsonValueKind.Number ? y.GetInt32().ToString() : y.GetString();
            year = MusicBrainzClient.ExtractYear(year);
        }

        var genre = FirstArrayString(root, "genres") ?? FirstArrayString(root, "genre");
        var style = FirstArrayString(root, "styles") ?? FirstArrayString(root, "style");
        var country = root.TryGetProperty("country", out var c) ? c.GetString() : null;

        string? format = null;
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in formats.EnumerateArray())
            {
                if (f.TryGetProperty("name", out var fn))
                {
                    format = fn.GetString();
                    break;
                }
            }
        }

        string? label = null;
        if (root.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in labels.EnumerateArray())
            {
                if (l.TryGetProperty("name", out var ln))
                {
                    label = ln.GetString();
                    break;
                }
            }
        }

        var tracks = ExtractTracks(root);

        return new DiscogsReleaseCandidate
        {
            Id = id,
            Title = title,
            Artist = artist,
            Album = string.IsNullOrWhiteSpace(title) ? "" : title,
            Year = year,
            Genre = genre ?? style,
            Style = style,
            Country = country,
            Format = format,
            Label = label,
            Tracks = tracks
        };
    }

    public static (string Artist, string Album) SplitDiscogsTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return ("", "");
        var parts = Regex.Split(title, @"\s+-\s+", RegexOptions.Compiled);
        if (parts.Length >= 2)
            return (parts[0].Trim(), string.Join(" - ", parts.Skip(1)).Trim());
        return ("", title.Trim());
    }

    public static List<DiscogsTrackInfo> ExtractTracks(JsonElement root)
    {
        var result = new List<DiscogsTrackInfo>();
        if (!root.TryGetProperty("tracklist", out var tracklist) || tracklist.ValueKind != JsonValueKind.Array)
            return result;

        var num = 1;
        foreach (var tr in tracklist.EnumerateArray())
        {
            var type = tr.TryGetProperty("type_", out var ty) ? ty.GetString() : "track";
            if (!string.Equals(type, "track", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(type)
                && !string.Equals(type, "index", StringComparison.OrdinalIgnoreCase))
                continue;

            var title = tr.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var position = tr.TryGetProperty("position", out var pos) ? pos.GetString() : null;
            var duration = tr.TryGetProperty("duration", out var dur) ? dur.GetString() : null;

            var trackNum = num;
            if (!string.IsNullOrWhiteSpace(position))
            {
                var digits = new string(position.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var parsed) && parsed > 0)
                    trackNum = parsed;
            }

            result.Add(new DiscogsTrackInfo
            {
                Number = trackNum,
                Title = title,
                Duration = duration,
                Position = position
            });
            num++;
        }

        if (result.Count > 1 && result.Select(t => t.Number).Distinct().Count() < result.Count)
        {
            for (var i = 0; i < result.Count; i++)
                result[i].Number = i + 1;
        }

        return result;
    }

    private static string ExtractArtists(JsonElement root)
    {
        if (!root.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
            return "";

        var parts = new List<string>();
        foreach (var a in artists.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            // Discogs often appends " (n)" for disambiguation
            name = Regex.Replace(name, @"\s*\(\d+\)\s*$", "").Trim();
            var join = a.TryGetProperty("join", out var j) ? j.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(name))
                parts.Add(name + join);
        }
        return string.Concat(parts).Trim();
    }

    private static string? FirstArrayString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var arr)) return null;
        if (arr.ValueKind == JsonValueKind.String) return arr.GetString();
        if (arr.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static void EnsureToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "Discogs token not set. Create a personal access token at https://www.discogs.com/settings/developers and enter it in Settings.");
    }

    private async Task<string> GetStringAsync(string url, string token, CancellationToken ct)
    {
        await EnforceRateLimitAsync(ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Discogs", $"token={token.Trim()}");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Discogs HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        return body;
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
