using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace PriceTracker.Services;

/// Optional: Steam all-time lows from IsThereAnyDeal (free API key from isthereanydeal.com/apps).
/// Without a key the app uses the price history it records itself.
public static class ItadService
{
    private const string Api = "https://api.isthereanydeal.com";
    private const int SteamShopId = 61;

    public static async Task<Dictionary<int, decimal>> GetSteamLowsAsync(string key, IEnumerable<int> appIds, string country, CancellationToken ct = default)
    {
        var result = new Dictionary<int, decimal>();
        if (string.IsNullOrWhiteSpace(key)) return result;

        var ids = new Dictionary<string, int>();
        foreach (var appId in appIds)
        {
            try
            {
                var json = await Http.Client.GetStringAsync($"{Api}/games/lookup/v1?key={Uri.EscapeDataString(key)}&appid={appId}", ct);
                var id = (string?)JsonNode.Parse(json)?["game"]?["id"];
                if (id != null) ids[id] = appId;
            }
            catch (HttpRequestException) { }
        }
        if (ids.Count == 0) return result;

        foreach (var chunk in ids.Keys.Chunk(100))
        {
            using var resp = await Http.Client.PostAsJsonAsync(
                $"{Api}/games/storelow/v2?key={Uri.EscapeDataString(key)}&country={country}&shops={SteamShopId}", chunk, ct);
            if (!resp.IsSuccessStatusCode) continue;
            var arr = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))?.AsArray();
            if (arr == null) continue;
            foreach (var g in arr)
            {
                var gid = (string?)g?["id"];
                if (gid == null || !ids.TryGetValue(gid, out var appId)) continue;
                var amount = g?["lows"]?.AsArray().Select(l => (decimal?)l?["price"]?["amount"]).Where(a => a.HasValue).Min();
                if (amount.HasValue) result[appId] = amount.Value;
            }
        }
        return result;
    }

    /// ITAD's id for a Steam app (needed for every other call).
    public static async Task<string?> LookupAsync(string key, int appId, CancellationToken ct = default)
    {
        var json = await Http.Client.GetStringAsync($"{Api}/games/lookup/v1?key={Uri.EscapeDataString(key)}&appid={appId}", ct);
        return (string?)JsonNode.Parse(json)?["game"]?["id"];
    }

    /// Steam's own price history (every price change ITAD has seen on Steam, years back).
    public static async Task<List<PriceTracker.Models.PricePoint>> GetSteamHistoryAsync(string key, string itadId, string country, CancellationToken ct = default)
    {
        var url = $"{Api}/games/history/v2?key={Uri.EscapeDataString(key)}&id={Uri.EscapeDataString(itadId)}&country={country}&shops={SteamShopId}&since={Uri.EscapeDataString("2005-01-01T00:00:00Z")}";
        var json = await Http.Client.GetStringAsync(url, ct);
        var rows = new List<(DateTime T, decimal P)>();
        foreach (var e in JsonNode.Parse(json)?.AsArray() ?? new JsonArray())
        {
            var ts = (string?)e?["timestamp"];
            var amount = (decimal?)e?["deal"]?["price"]?["amount"];
            if (ts == null || amount == null || !DateTimeOffset.TryParse(ts, out var t)) continue;
            rows.Add((t.LocalDateTime, Math.Round(amount.Value, 2)));
        }
        var result = new List<PriceTracker.Models.PricePoint>();
        foreach (var r in rows.OrderBy(r => r.T))
            if (result.Count == 0 || result[^1].A != r.P)
                result.Add(new PriceTracker.Models.PricePoint { Time = r.T, A = r.P });
        return result;
    }
}
