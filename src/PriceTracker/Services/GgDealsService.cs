using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PriceTracker.Services;

public sealed record GgPrices(string? Url, decimal? CurrentKeyshops, decimal? HistoricalKeyshops, decimal? CurrentRetail, decimal? HistoricalRetail);

/// GG.deals official prices API (free key for personal use: gg.deals/api).
/// Gives the cheapest key-shop price plus real all-time lows for retail stores and key shops.
/// GG.deals asks for attribution, so the app links to GG.deals wherever this data is shown.
public static class GgDealsService
{
    public const string ApiPage = "https://gg.deals/api/";

    public static async Task<Dictionary<int, GgPrices>> GetPricesAsync(string key, IReadOnlyList<int> appIds, string country, CancellationToken ct = default)
    {
        var result = new Dictionary<int, GgPrices>();
        if (string.IsNullOrWhiteSpace(key) || appIds.Count == 0) return result;
        var region = country.ToLowerInvariant();

        foreach (var chunk in appIds.Chunk(100))
        {
            var url = $"https://api.gg.deals/v1/prices/by-steam-app-id/?ids={string.Join(',', chunk)}&key={Uri.EscapeDataString(key.Trim())}&region={region}";
            using var resp = await Http.Client.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var root = JsonNode.Parse(body);
            if (root?["success"]?.GetValue<bool>() != true)
            {
                var msg = (string?)root?["data"]?["message"] ?? $"HTTP {(int)resp.StatusCode}";
                throw new InvalidOperationException("GG.deals: " + msg);
            }
            if (root["data"] is not JsonObject data) continue;
            foreach (var (id, node) in data)
            {
                if (node is null || !int.TryParse(id, out var appId)) continue;   // unknown ids come back as null
                var p = node["prices"];
                result[appId] = new GgPrices(
                    (string?)node["url"],
                    Num(p?["currentKeyshops"]), Num(p?["historicalKeyshops"]),
                    Num(p?["currentRetail"]), Num(p?["historicalRetail"]));
            }
        }
        return result;
    }

    private static decimal? Num(JsonNode? n)
    {
        if (n is null) return null;
        try
        {
            return n.GetValueKind() switch
            {
                JsonValueKind.Number => n.GetValue<decimal>(),
                JsonValueKind.String => decimal.TryParse((string?)n, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null,
                _ => null,
            };
        }
        catch { return null; }
    }

    /// GG.deals' own price chart for a game: best official-store price (A) and best key-shop price (B)
    /// over time, often years back. Read from the game page exactly like the page's own chart.
    private const string HistoryExpression = """
        (async () => {
          const el = document.querySelector('#header-historical-chart-container');
          const url = el && (el.dataset.withKeyshopsUrl || el.dataset.withoutKeyshopsUrl);
          if (!url) return null;
          const r = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
          if (!r.ok) return null;
          const j = await r.json();
          return JSON.stringify(j.chartData || null);
        })()
        """;

    public static string GameUrl(int appId) => $"https://gg.deals/steam/app/{appId}/";

    public static async Task<List<PriceTracker.Models.PricePoint>?> GetHistoryAsync(string pageUrl, CancellationToken ct = default)
    {
        var (_, json) = await BrowserFetcher.Instance.RunWithFollowUpAsync(pageUrl, "null", HistoryExpression, ct);
        return json == null ? null : ParseHistory(json);
    }

    /// {retail:[{x,y}], keyshops:[{x,y}]} â†’ one step series; each point carries the latest value of both.
    public static List<PriceTracker.Models.PricePoint> ParseHistory(string json)
    {
        var result = new List<PriceTracker.Models.PricePoint>();
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null) return result;
            var events = new List<(long T, bool Key, decimal Y)>();
            void Read(string name, bool key)
            {
                if (root[name] is not JsonArray arr) return;
                foreach (var n in arr)
                    if (n?["x"] is { } x && n["y"] is { } y)
                    {
                        var v = (decimal)y.GetValue<double>();
                        if (v > 0) events.Add(((long)x.GetValue<double>(), key, Math.Round(v, 2)));
                    }
            }
            Read("retail", false);
            Read("keyshops", true);
            decimal? a = null, b = null;
            foreach (var grp in events.OrderBy(e => e.T).GroupBy(e => e.T))
            {
                foreach (var e in grp) { if (e.Key) b = e.Y; else a = e.Y; }
                var time = DateTimeOffset.FromUnixTimeMilliseconds(grp.Key).LocalDateTime;
                if (result.Count > 0 && result[^1].A == a && result[^1].B == b) continue;
                result.Add(new PriceTracker.Models.PricePoint { Time = time, A = a, B = b });
            }
        }
        catch { /* format changed â€“ own history still works */ }
        return result;
    }
}
