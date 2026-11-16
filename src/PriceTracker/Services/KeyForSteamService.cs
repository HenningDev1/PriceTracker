using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PriceTracker.Services;

public sealed record KeyOffer(string PageUrl, decimal? LowPrice, string? Shop, int Offers);

/// Key prices from keyforsteam.de (public product pages + their quick search).
/// Requests are spaced out so the site isn't hammered.
public static partial class KeyForSteamService
{
    private const string Base = "https://www.keyforsteam.de";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _last = DateTime.MinValue;
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(2.5);

    public static async Task<string?> FindPageAsync(string gameName, CancellationToken ct = default)
    {
        var url = $"{Base}/wp-admin/admin-ajax.php?action=quicksearch&search_name={Uri.EscapeDataString(gameName)}&currency=eur&locale=de_DE";
        var body = await GetAsync(url, "document.body.innerText", ct);
        if (string.IsNullOrWhiteSpace(body)) return null;
        string html;
        try { html = (string?)JsonNode.Parse(body)?["results"] ?? ""; }
        catch { return null; }

        var rows = new List<(string Url, string Slug, bool Pc)>();
        foreach (Match row in RowRx().Matches(html))
        {
            var block = row.Value;
            var href = HrefRx().Match(block);
            if (!href.Success) continue;
            var link = WebUtility.HtmlDecode(href.Groups[1].Value.Replace("\\/", "/"));
            var slug = SlugRx().Match(link) is { Success: true } s ? s.Groups[1].Value : "";
            bool pc = !block.Contains("data-platforms=") || Regex.IsMatch(block, "data-platforms=\"[^\"]*pc", RegexOptions.IgnoreCase);
            rows.Add((link, slug, pc));
        }
        if (rows.Count == 0) return null;

        var want = Slugify(gameName);
        var pcRows = rows.Where(r => r.Pc).ToList();
        if (pcRows.Count == 0) pcRows = rows;
        Func<(string Url, string Slug, bool Pc), bool>[] rules =
        {
            r => r.Slug == want,
            r => r.Slug.StartsWith(want + "-", StringComparison.Ordinal) && !IsEdition(r.Slug, want),
            r => r.Slug.Length > 3 && want.StartsWith(r.Slug, StringComparison.Ordinal),
            r => Similar(r.Slug, want),
        };
        foreach (var rule in rules)
            foreach (var r in pcRows)
                if (rule(r)) return r.Url;
        return null;
    }

    public static async Task<KeyOffer?> GetOffersAsync(string pageUrl, CancellationToken ct = default)
    {
        var html = await GetAsync(pageUrl, "document.documentElement.outerHTML", ct);
        if (html == null) return null;

        foreach (Match m in LdRx().Matches(html))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(m.Groups[1].Value); }
            catch { continue; }
            var agg = FindAggregate(node);
            if (agg == null) continue;

            var offers = agg["offers"]?.AsArray()
                .Select(o => (Price: Http.ParsePrice(o?["price"]?.ToString()), Shop: (string?)o?["seller"]?["name"]))
                .Where(o => o.Price.HasValue)
                .ToList() ?? new();
            var best = offers.OrderBy(o => o.Price).FirstOrDefault();
            decimal? low = best.Price ?? Http.ParsePrice(agg["lowPrice"]?.ToString());
            int count = (int?)agg["offerCount"] ?? offers.Count;
            return new KeyOffer(pageUrl, low, best.Shop, count);
        }
        return new KeyOffer(pageUrl, null, null, 0);
    }

    private static JsonNode? FindAggregate(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                if ((string?)o["@type"] == "AggregateOffer") return o;
                foreach (var kv in o)
                    if (FindAggregate(kv.Value) is { } f) return f;
                break;
            case JsonArray a:
                foreach (var x in a)
                    if (FindAggregate(x) is { } f) return f;
                break;
        }
        return null;
    }

    private static DateTime _downUntil = DateTime.MinValue;

    /// Plain HTTP first; if the site refuses (bot protection), the embedded browser is used instead.
    /// If the site can't be reached at all, key lookups pause for 15 minutes instead of stalling every game.
    private static async Task<string?> GetAsync(string url, string browserScript, CancellationToken ct)
    {
        if (DateTime.UtcNow < _downUntil)
            throw new InvalidOperationException("KeyForSteam is not reachable right now – will retry later.");
        await Gate.WaitAsync(ct);
        try
        {
            var wait = _last + Spacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var resp = await Http.Client.GetAsync(url, timeout.Token);
                if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // No connection at all: a browser wouldn't get through either.
                _downUntil = DateTime.UtcNow.AddMinutes(15);
                throw new InvalidOperationException("KeyForSteam is not reachable right now – will retry later.");
            }

            if (!BrowserFetcher.IsAvailable) return null;
            return await BrowserFetcher.Instance.RunAsync(url, browserScript, ct);
        }
        finally
        {
            _last = DateTime.UtcNow;
            Gate.Release();
        }
    }

    public static string Slugify(string name)
    {
        var s = name.ToLowerInvariant()
            .Replace("&", " ").Replace("'", "").Replace("’", "").Replace("®", "").Replace("™", "");
        s = Regex.Replace(s, "[^a-z0-9]+", "-").Trim('-');
        return s;
    }

    private static bool IsEdition(string slug, string want)
    {
        var rest = slug[(want.Length + 1)..];
        return Regex.IsMatch(rest, @"^(deluxe|gold|ultimate|complete|premium|season|dlc|soundtrack|collectors|digital|goty|game-of-the-year|definitive|enhanced|expansion|bundle|pack)");
    }

    private static bool Similar(string a, string b)
    {
        var ta = a.Split('-').ToHashSet();
        var tb = b.Split('-').ToHashSet();
        if (tb.Count == 0) return false;
        return ta.Intersect(tb).Count() >= Math.Max(1, (int)Math.Ceiling(tb.Count * 0.6));
    }

    [GeneratedRegex(@"<li[^>]*ls-results-row[^>]*>.*?</li>", RegexOptions.Singleline)]
    private static partial Regex RowRx();
    [GeneratedRegex(@"href=""([^""]+)""")]
    private static partial Regex HrefRx();
    [GeneratedRegex(@"keyforsteam\.de/([a-z0-9-]+?)-key-kaufen-preisvergleich")]
    private static partial Regex SlugRx();
    [GeneratedRegex(@"<script[^>]*application/ld\+json[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex LdRx();
}
