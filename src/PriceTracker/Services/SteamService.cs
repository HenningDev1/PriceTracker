using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PriceTracker.Services;

public sealed record SteamApp(int AppId, string Name, string? Cover, decimal? Price, decimal? Regular, int Discount, bool IsFree, bool Unreleased);
public sealed record SteamSearchHit(int AppId, string Name, string? Image, decimal? Price);

/// Public Steam endpoints only (no API key needed):
///   IWishlistService/GetWishlist, IStoreBrowseService/GetItems and the store search.
public static partial class SteamService
{
    private const string AssetBase = "https://shared.akamai.steamstatic.com/store_item_assets/";

    public static string StoreUrl(int appId) => $"https://store.steampowered.com/app/{appId}/";
    public static string SteamDbUrl(int appId) => $"https://steamdb.info/app/{appId}/";

    /// Accepts a SteamID64, a profile URL (/profiles/… or /id/…) or a bare custom name.
    public static async Task<string> ResolveSteamIdAsync(string input, CancellationToken ct = default)
    {
        input = input.Trim().TrimEnd('/');
        if (Id64Rx().Match(input) is { Success: true } m) return m.Value;
        var vanity = input;
        var idx = input.IndexOf("/id/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) vanity = input[(idx + 4)..].Split('/')[0];
        if (string.IsNullOrWhiteSpace(vanity)) throw new InvalidOperationException("Enter your Steam profile link or SteamID64 in Settings.");

        var xml = await Http.Client.GetStringAsync($"https://steamcommunity.com/id/{Uri.EscapeDataString(vanity)}?xml=1", ct);
        var id = Regex.Match(xml, @"<steamID64>(\d{17})</steamID64>");
        if (!id.Success) throw new InvalidOperationException($"Steam profile \"{vanity}\" was not found.");
        return id.Groups[1].Value;
    }

    public static async Task<List<int>> GetWishlistAsync(string steamId64, CancellationToken ct = default)
    {
        var json = await Http.Client.GetStringAsync(
            $"https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid={steamId64}", ct);
        var items = JsonNode.Parse(json)?["response"]?["items"]?.AsArray();
        if (items == null)
            throw new InvalidOperationException("The wishlist is empty or private. Set \"Game details\" to Public in your Steam privacy settings.");
        return items.Select(i => (int?)i?["appid"]).Where(i => i.HasValue).Select(i => i!.Value).ToList();
    }

    /// Batch lookup of names, cover art and current prices (up to ~50 ids per call).
    public static async Task<List<SteamApp>> GetAppsAsync(IReadOnlyList<int> appIds, string country, CancellationToken ct = default)
    {
        var result = new List<SteamApp>();
        foreach (var chunk in appIds.Chunk(40))
        {
            var input = new JsonObject
            {
                ["ids"] = new JsonArray(chunk.Select(id => (JsonNode)new JsonObject { ["appid"] = id }).ToArray()),
                ["context"] = new JsonObject { ["language"] = "english", ["country_code"] = country },
                ["data_request"] = new JsonObject { ["include_assets"] = true, ["include_release"] = true },
            };
            var url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" +
                      Uri.EscapeDataString(input.ToJsonString());
            var json = await Http.Client.GetStringAsync(url, ct);
            var items = JsonNode.Parse(json)?["response"]?["store_items"]?.AsArray();
            if (items == null) continue;

            // appdetails' price_overview is the most reliable source for regular price + discount.
            JsonNode? overview = null;
            try
            {
                var po = await Http.Client.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={string.Join(',', chunk)}&cc={country}&filters=price_overview", ct);
                overview = JsonNode.Parse(po);
            }
            catch { /* GetItems prices are good enough */ }

            foreach (var it in items)
            {
                if (it == null || (int?)it["success"] != 1) continue;
                int appId = (int?)it["appid"] ?? (int?)it["id"] ?? 0;
                string name = (string?)it["name"] ?? $"App {appId}";
                string? cover = null;
                var assets = it["assets"];
                var fmt = (string?)assets?["asset_url_format"];
                var header = (string?)assets?["header"];
                if (fmt != null && header != null) cover = AssetBase + fmt.Replace("${FILENAME}", header);

                var bpo = it["best_purchase_option"];
                decimal? price = Cents(bpo?["final_price_in_cents"]);
                decimal? regular = Cents(bpo?["original_price_in_cents"]) ?? price;
                int discount = (int?)bpo?["discount_pct"] ?? 0;
                var ov = overview?[appId.ToString()]?["data"] is JsonObject od ? od["price_overview"] : null;
                if (ov != null)
                {
                    price = Cents(ov["final"]) ?? price;
                    regular = Cents(ov["initial"]) ?? regular;
                    discount = (int?)ov["discount_percent"] ?? discount;
                }
                if (discount == 0 && price.HasValue && regular is > 0 && regular > price)
                    discount = (int)Math.Round((1 - price.Value / regular.Value) * 100);
                bool isFree = (bool?)it["is_free"] ?? false;
                bool coming = (bool?)it["release"]?["is_coming_soon"] ?? false;
                result.Add(new SteamApp(appId, name, cover, price, regular, discount, isFree, coming && price == null));
            }
        }
        return result;
    }

    public static async Task<List<SteamSearchHit>> SearchAsync(string term, string country, CancellationToken ct = default)
    {
        var json = await Http.Client.GetStringAsync(
            $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term)}&cc={country}&l=english", ct);
        var items = JsonNode.Parse(json)?["items"]?.AsArray() ?? new JsonArray();
        return items.Where(i => (string?)i?["type"] == "app").Select(i => new SteamSearchHit(
            (int)i!["id"]!,
            (string?)i["name"] ?? "",
            (string?)i["tiny_image"],
            Cents(i["price"]?["final"]))).ToList();
    }

    private static decimal? Cents(JsonNode? n)
    {
        if (n == null) return null;
        try
        {
            long v = n.GetValueKind() == JsonValueKind.String ? long.Parse((string)n!) : (long)n;
            return v / 100m;
        }
        catch { return null; }
    }

    [GeneratedRegex(@"\b7656\d{13}\b")]
    private static partial Regex Id64Rx();
}
