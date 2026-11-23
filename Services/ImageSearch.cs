// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jotanator;

/// <summary>Image search providers + the fallback chain. No Discord code in here (port of services/image_search.py).</summary>
public static class ImageSearch
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    private static readonly Regex ImgRe = new(
        @"https?://[^""'\s\\<>]+?\.(?:jpe?g|png|webp|gif)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BingRe = new(@"murl&quot;:&quot;(.*?)&quot;", RegexOptions.Compiled);
    private static readonly Regex VqdRe = new(@"vqd=[""']?([\d-]+)[""']?", RegexOptions.Compiled);

    private delegate Task<List<string>> Provider(HttpClient http, string query, CancellationToken ct);

    // Order matters: this is the fallback chain.
    private static readonly List<(string Name, Provider Fn)> Providers = BuildProviders();

    private static List<(string Name, Provider Fn)> BuildProviders()
    {
        var list = new List<(string Name, Provider Fn)>
        {
            ("Google", new Provider(GoogleImages)),
        };

        if (!string.IsNullOrEmpty(Config.SerpApiKey))
            list.Add(("Google (API)", new Provider(SerperImages)));
        else
            Console.WriteLine("SERP_API_KEY not set: API fallback disabled.");

        list.Add(("Bing", new Provider(BingImages)));
        list.Add(("DuckDuckGo", new Provider(DdgImages)));
        return list;
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        return req;
    }

    private static async Task<string> GetTextAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Get, url);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    // ------------------------------------------------------------ providers

    private static async Task<List<string>> GoogleImages(HttpClient http, string query, CancellationToken ct)
    {
        var url = $"https://www.google.com/search?q={WebUtility.UrlEncode(query)}&tbm=isch&safe=active";
        var text = await GetTextAsync(http, url, ct);
        text = text.Replace("\\u003d", "=").Replace("\\u0026", "&");

        return ImgRe.Matches(text)
            .Select(m => m.Value)
            .Where(u => !u.Contains("gstatic.com") && !u.Contains("google."))
            .ToList();
    }

    /// <summary>Google Images results via Serper.dev (needs SERP_API_KEY).</summary>
    private static async Task<List<string>> SerperImages(HttpClient http, string query, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/images");
        req.Headers.TryAddWithoutValidation("X-API-KEY", Config.SerpApiKey);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { q = query, num = Config.MaxResults }),
            Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");

        var result = new List<string>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in images.EnumerateArray())
                if (item.TryGetProperty("imageUrl", out var u) && u.ValueKind == JsonValueKind.String)
                    result.Add(u.GetString()!);
        }
        return result;
    }

    private static async Task<List<string>> BingImages(HttpClient http, string query, CancellationToken ct)
    {
        var url = $"https://www.bing.com/images/search?q={WebUtility.UrlEncode(query)}&adlt=strict";
        var text = await GetTextAsync(http, url, ct);
        return BingRe.Matches(text).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
    }

    /// <summary>
    /// DuckDuckGo images. The Python bot used the "ddgs" package; there is no .NET equivalent,
    /// so this talks to DDG's image endpoint directly (vqd token, then i.js).
    /// </summary>
    private static async Task<List<string>> DdgImages(HttpClient http, string query, CancellationToken ct)
    {
        var q = WebUtility.UrlEncode(query);

        var page = await GetTextAsync(http, $"https://duckduckgo.com/?q={q}&iax=images&ia=images", ct);
        var vqd = VqdRe.Match(page);
        if (!vqd.Success) throw new InvalidOperationException("DuckDuckGo vqd token not found");

        var url = $"https://duckduckgo.com/i.js?o=json&q={q}&l=us-en&vqd={vqd.Groups[1].Value}&f=,,,,,&p=1";
        using var req = NewRequest(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri("https://duckduckgo.com/");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);

        var result = new List<string>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("image", out var u) && u.ValueKind == JsonValueKind.String)
                    result.Add(u.GetString()!);
                if (result.Count >= Config.MaxResults) break;
            }
        }
        return result;
    }

    // ------------------------------------------------------------ public API

    public static async Task<bool> IsValidImageAsync(HttpClient http, string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var req = NewRequest(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return resp.StatusCode == HttpStatusCode.OK &&
                   (resp.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.Ordinal) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHttpUrl(string url) =>
        url.Length <= 2000 &&
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Tries the preferred provider first, then the rest in chain order.
    /// Returns (source, urls, index of the first valid image).
    /// </summary>
    public static async Task<(string? Source, List<string> Urls, int Start)> FindImagesAsync(
        HttpClient http, string query, string preferred = "Google")
    {
        var order = Providers.Where(p => p.Name == preferred)
            .Concat(Providers.Where(p => p.Name != preferred))
            .ToList();

        foreach (var (name, fn) in order)
        {
            List<string> found;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Config.TimeoutSeconds));
                found = await fn(http, query, cts.Token);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{name}] failed: {e.GetType().Name}: {e.Message}");
                continue;
            }

            // dedupe (keeping order); Discord rejects malformed embed URLs, so drop those too
            var urls = found.Distinct().Where(IsHttpUrl).Take(Config.MaxResults).ToList();
            if (urls.Count == 0)
            {
                Console.WriteLine($"[{name}] no results");
                continue;
            }

            for (var i = 0; i < Math.Min(5, urls.Count); i++)
                if (await IsValidImageAsync(http, urls[i]))
                    return (name, urls, i);

            Console.WriteLine($"[{name}] no loadable images");
        }

        return (null, new List<string>(), 0);
    }
}
