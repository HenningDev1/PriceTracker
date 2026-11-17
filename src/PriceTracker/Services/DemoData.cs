using PriceTracker.Models;

namespace PriceTracker.Services;

/// Sample data used only by scripts/screenshots.ps1 (PRICETRACKER_DEMO=1 with a temporary data folder).
/// Prices are illustrative, not real quotes.
public static class DemoData
{
    public static void Write()
    {
        var rnd = new Random(7);
        var data = new AppData { LastRefresh = DateTime.Now.AddMinutes(-12) };

        GameEntry Game(int id, string name, decimal regular, int discount, decimal? key, string shop, int offers, decimal atl, bool wishlist = true, bool keyLow = false)
        {
            var price = Math.Round(regular * (100 - discount) / 100m, 2);
            var g = new GameEntry
            {
                AppId = id, Name = name, FromWishlist = wishlist,
                CoverUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{id}/header.jpg",
                SteamPrice = price, SteamRegular = regular, Discount = discount,
                KeyPrice = key, KeyShop = shop, KeyOffers = offers,
                KeyPageUrl = $"https://www.keyforsteam.de/{KeyForSteamService.Slugify(name)}-key-kaufen-preisvergleich/",
                LastChecked = DateTime.Now.AddMinutes(-12), Added = DateTime.Now.AddDays(-rnd.Next(30, 200)),
            };
            // ~5 months of history: regular price with a couple of sales, keys drifting down.
            var start = DateTime.Now.AddDays(-150);
            decimal k = (key ?? regular * 0.7m) * 1.35m;
            for (int d = 0; d <= 150; d += 3)
            {
                var t = start.AddDays(d);
                decimal steam = regular;
                if (d is >= 40 and < 54) steam = Math.Round(regular * 0.7m, 2);
                if (d is >= 100 and < 112) steam = Math.Max(atl, Math.Round(regular * 0.6m, 2));
                if (d >= 147) steam = price;
                k = Math.Max(key ?? 0, Math.Round(k - (decimal)rnd.NextDouble() * 0.9m + 0.25m, 2));
                if (d >= 147 && key.HasValue) k = key.Value;
                g.History.Add(new PricePoint { Time = t, A = steam, B = key.HasValue ? k : null });
            }
            if (!keyLow && key.HasValue)
                g.History[g.History.Count / 2].B = Math.Round(key.Value * 0.93m, 2);   // keys were cheaper once
            if (discount > 0 && price <= atl) { /* current price is the ATL */ }
            else g.SteamHistoricLow = atl;
            return g;
        }

        data.Games.AddRange(new[]
        {
            Game(1091500, "Cyberpunk 2077", 59.99m, 65, 17.85m, "Gamivo", 112, 21.00m),
            Game(1174180, "Red Dead Redemption 2", 59.99m, 75, 13.20m, "K4G", 96, 14.99m, keyLow: true),
            Game(2358720, "Black Myth: Wukong", 59.99m, 0, 27.90m, "Eneba", 74, 41.99m),
            Game(1245620, "ELDEN RING", 59.99m, 40, 27.49m, "GAMESEAL", 149, 29.99m),
            Game(1086940, "Baldur's Gate 3", 59.99m, 0, 41.50m, "Kinguin", 88, 47.99m),
            Game(1145350, "Hades II", 29.50m, 0, 24.10m, "Gamivo", 31, 29.50m),
            Game(413150, "Stardew Valley", 13.99m, 0, 9.79m, "Driffle", 54, 6.99m),
            Game(1030300, "Hollow Knight: Silksong", 19.50m, 0, 15.10m, "K4G", 40, 19.50m, wishlist: false),
        });

        ItemEntry Item(string name, string url, string img, decimal usual, decimal now, decimal? used, string? usedLabel, string shop, int offers, bool newLow = false, int days = 120)
        {
            var i = new ItemEntry
            {
                Name = name, Url = url, ImageUrl = img, NewPrice = now, NewShop = shop, OfferCount = offers,
                UsedPrice = used, UsedLabel = usedLabel, UsedUrl = used.HasValue ? url : null,
                LastChecked = DateTime.Now.AddMinutes(-12), Added = DateTime.Now.AddDays(-days),
            };
            var start = DateTime.Now.AddDays(-days);
            decimal p = usual * 1.04m, u = (used ?? 0) * 1.15m;
            for (int d = 0; d < days; d += 6)
            {
                // A calm random walk around the usual price, like real retail prices.
                if (rnd.NextDouble() < 0.55) p = Math.Round(Math.Clamp(p + usual * (decimal)(rnd.NextDouble() - 0.5) * 0.05m, usual * 0.95m, usual * 1.07m), 2);
                if (rnd.NextDouble() < 0.4) u = Math.Round(Math.Max((used ?? 0) * 1.02m, u - (used ?? 0) * (decimal)rnd.NextDouble() * 0.03m), 2);
                i.History.Add(new PricePoint { Time = start.AddDays(d), A = p, B = used.HasValue ? u : null });
            }
            if (!newLow)
            {
                // An earlier sale that was cheaper than today, so today is not an all-time low.
                var mid = i.History[i.History.Count / 2];
                mid.A = Math.Round(now * 0.94m, 2);
            }
            i.History.Add(new PricePoint { Time = DateTime.Now.AddMinutes(-12), A = now, B = used });
            return i;
        }

        data.Items.AddRange(new[]
        {
            Item("Sony WH-1000XM5 schwarz", "https://geizhals.de/sony-wh-1000xm5-schwarz-a2733874.html",
                 "https://gzhls.at/pix/d5/3a/d53a0512e3bbb23d-m.webp", 259m, 195.90m, 149.00m, "Wie neu, mit Garantie", "Alternate", 64, newLow: true),
            Item("LG UltraGear OLED 27GS95QE-B", "https://geizhals.de/lg-ultragear-oled-27gs95qe-b-a3091299.html",
                 "https://gzhls.at/pix/52/7f/527f4c79fa61875a-m.webp", 649m, 545.00m, null, null, "Cyberport", 38, newLow: true),
            Item("Apple iPhone 15 128GB blau", "https://geizhals.de/apple-iphone-15-128gb-blau-a3020646.html",
                 "https://gzhls.at/pix/8f/d6/8fd6d17ac20f45aa-n.webp", 669m, 649.00m, 389.00m, "Wie neu, mit Garantie", "kron_trade", 16),
            Item("Samsung SSD 990 PRO 2TB", "https://geizhals.de/samsung-ssd-990-pro-2tb-mz-v9p2t0bw-a2795958.html",
                 "https://gzhls.at/pix/9b/74/9b743bc70d267102-m.webp", 389m, 395.99m, null, null, "Mindfactory", 71),
            Item("Sony PlayStation 5 Pro 2TB weiß", "https://geizhals.de/sony-playstation-5-pro-2tb-weiss-a3298140.html",
                 "https://gzhls.at/pix/76/f8/76f829bcabff6ab4-m.webp", 799m, 749.00m, 619.00m, "Sehr gut, refurbished", "MediaMarkt", 27),
            Item("Zotac Gaming GeForce RTX 4070 Ti SUPER Trinity Black Edition", "https://geizhals.de/zotac-gaming-geforce-rtx-4070-ti-super-trinity-black-edition-zt-d40730d-10p-a3103639.html",
                 "https://gzhls.at/pix/d4/a0/d4a0b3f56b37749b-m.webp", 899m, 879.00m, null, null, "Caseking", 12),
            Item("GIGABYTE GeForce RTX 4070 Ti SUPER Gaming OC 16G", "https://geizhals.de/gigabyte-geforce-rtx-4070-ti-super-gaming-oc-16g-gv-n407tsgaming-oc-16gd-a3097288.html",
                 "https://gzhls.at/pix/fd/8c/fd8c834f775cb424-m.webp", 929m, 899.00m, 789.00m, "B-Ware", "Alternate", 9),
            Item("GIGABYTE GeForce RTX 4070 SUPER Windforce OC 12G", "https://geizhals.de/gigabyte-geforce-rtx-4070-super-windforce-oc-12g-gv-n407swf3oc-12gd-a3097423.html",
                 "https://gzhls.at/pix/26/c7/26c778753b5d2d3c-m.webp", 649m, 599.00m, null, null, "Mindfactory", 21, newLow: true),
        });

        foreach (var it in data.Items.Where(x => x.Name.Contains("RTX 4070"))) it.Folder = "RTX 4070 (Ti) SUPER";
        var settings = Storage.LoadSettings();
        settings.SteamProfile = "https://steamcommunity.com/id/example";
        Storage.SaveData(data);
        Storage.SaveSettings(settings);
    }
}
