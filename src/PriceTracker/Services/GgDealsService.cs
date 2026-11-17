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
}
