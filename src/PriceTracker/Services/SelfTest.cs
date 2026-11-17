using System.Text;

namespace PriceTracker.Services;

/// Live check of every data source. Run with PRICETRACKER_SELFTEST=<log file>
/// (see scripts\selftest.ps1). Writes a readable report and exits.
public static class SelfTest
{
    public static async Task RunAsync(string logPath)
    {
        var log = new StringBuilder();
        void Line(string s) { log.AppendLine(s); File.WriteAllText(logPath, log.ToString()); }

        async Task Step(string name, Func<Task<string>> f)
        {
            try { Line($"[ OK ] {name}: {await f()}"); }
            catch (Exception ex) { Line($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}"); }
        }

        await Step("Steam store search", async () =>
        {
            var hits = await SteamService.SearchAsync("elden ring", "DE");
            return $"{hits.Count} hits, first = {hits.FirstOrDefault()?.Name} ({hits.FirstOrDefault()?.AppId})";
        });
        await Step("Steam prices (GetItems + appdetails)", async () =>
        {
            var apps = await SteamService.GetAppsAsync(new[] { 1245620, 1091500, 570 }, "DE");
            return string.Join("; ", apps.Select(a => $"{a.Name} {Format.Money(a.Price)} (reg {Format.Money(a.Regular)}, -{a.Discount}%, free={a.IsFree})"));
        });
        await Step("Steam vanity → SteamID64", async () => await SteamService.ResolveSteamIdAsync("https://steamcommunity.com/id/gabelogannewell/"));
        await Step("Steam wishlist endpoint", async () =>
        {
            try { return $"{(await SteamService.GetWishlistAsync("76561197960287930")).Count} games"; }
            catch (InvalidOperationException ex) { return "reachable (" + ex.Message + ")"; }
        });

        string? kfs = null;
        await Step("KeyForSteam search", async () =>
        {
            kfs = await KeyForSteamService.FindPageAsync("ELDEN RING");
            return kfs ?? throw new Exception("no page found");
        });
        if (kfs != null)
            await Step("KeyForSteam offers", async () =>
            {
                var o = await KeyForSteamService.GetOffersAsync(kfs);
                return $"{Format.Money(o?.LowPrice)} at {o?.Shop}, {o?.Offers} offers";
            });

        Line($"WebView2 available: {BrowserFetcher.IsAvailable}");
        GhSearchHit? first = null;
        await Step("Geizhals search", async () =>
        {
            var hits = await GeizhalsService.SearchAsync("geizhals.de", "sony wh-1000xm5");
            first = hits.FirstOrDefault(h => !h.Variants) ?? hits.FirstOrDefault();
            return $"{hits.Count} hits, first = {first?.Name} {first?.PriceText} {first?.Url}";
        });
        if (first != null)
            await Step("Geizhals product", async () =>
            {
                var p = await GeizhalsService.GetProductAsync(first.Url) ?? throw new Exception("no data");
                var best = p.Offers.Where(o => o.Price.HasValue).OrderBy(o => o.Price).FirstOrDefault();
                return $"{p.Name}: new {Format.Money(best?.Price)} at {best?.Shop} ({p.Offers.Count} offers), used {Format.Money(p.Used?.Price)} {p.Used?.Label}";
            });
        await Step("Geizhals product with refurbished offers", async () =>
        {
            var p = await GeizhalsService.GetProductAsync("https://geizhals.de/apple-iphone-15-128gb-blau-a3020646.html") ?? throw new Exception("no data");
            return $"{p.Name}: new from {Format.Money(p.Offers.Where(o => o.Price.HasValue).Min(o => o.Price))}, used {Format.Money(p.Used?.Price)} ({p.Used?.Label})";
        });
        await Step("Geizhals price history", async () =>
        {
            var (p, h) = await GeizhalsService.GetProductWithHistoryAsync("https://geizhals.de/sony-wh-1000xm5-schwarz-a2733874.html", includeHistory: true);
            if (h == null || h.Count == 0) throw new Exception("no history returned");
            return $"{p?.Name}: {h.Count} price changes since {h[0].Time:yyyy-MM-dd}, low {Format.Money(h.Min(x => x.A))}, high {Format.Money(h.Max(x => x.A))}, latest {Format.Money(h[^1].A)}";
        });
        Line("done");
    }
}
