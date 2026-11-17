namespace PriceTracker.Models;

/// One recorded observation. For games A = Steam price, B = cheapest key.
/// For items A = cheapest new price, B = cheapest used/refurbished price.
public sealed class PricePoint
{
    public DateTime Time { get; set; }
    public decimal? A { get; set; }
    public decimal? B { get; set; }
}

public sealed class GameEntry
{
    public int AppId { get; set; }
    public string Name { get; set; } = "";
    public string? CoverUrl { get; set; }
    public bool FromWishlist { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;

    // Steam
    public decimal? SteamPrice { get; set; }
    public decimal? SteamRegular { get; set; }
    public int Discount { get; set; }
    public bool IsFree { get; set; }
    public bool Unreleased { get; set; }

    // Real all-time lows from GG.deals / IsThereAnyDeal (optional API keys)
    public decimal? SteamHistoricLow { get; set; }
    public decimal? KeyHistoricLow { get; set; }
    public string? GgDealsUrl { get; set; }
    public string? KeySource { get; set; }
    /// GG.deals' long-term chart: A = best official-store price, B = best key price.
    public List<PricePoint> GgHistory { get; set; } = new();
    public DateTime? GgHistoryFetched { get; set; }
    /// Steam's own price history from IsThereAnyDeal (A only).
    public List<PricePoint> SteamHistory { get; set; } = new();
    public DateTime? SteamHistoryFetched { get; set; }
    public string? ItadId { get; set; }

    // KeyForSteam
    public string? KeyPageUrl { get; set; }
    public decimal? KeyPrice { get; set; }
    public string? KeyShop { get; set; }
    public int KeyOffers { get; set; }

    public DateTime? LastChecked { get; set; }
    public string? LastError { get; set; }
    public List<PricePoint> History { get; set; } = new();
}

public sealed class ItemEntry
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ImageUrl { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;
    /// Optional folder, e.g. "MacBook Neo" holding every colour / storage variant.
    public string? Folder { get; set; }

    public decimal? NewPrice { get; set; }
    public string? NewShop { get; set; }
    public int OfferCount { get; set; }

    public decimal? UsedPrice { get; set; }
    public string? UsedLabel { get; set; }
    public string? UsedUrl { get; set; }

    public DateTime? LastChecked { get; set; }
    public string? LastError { get; set; }
    public List<PricePoint> History { get; set; } = new();

    /// Geizhals' own daily best-price history (only the days the price changed), A = new price.
    public List<PricePoint> GeizhalsHistory { get; set; } = new();
    public DateTime? GeizhalsHistoryFetched { get; set; }
}

public sealed class AppData
{
    public List<GameEntry> Games { get; set; } = new();
    public List<ItemEntry> Items { get; set; } = new();
    public DateTime? LastRefresh { get; set; }
}

public sealed class AppSettings
{
    // Appearance
    public string ThemeMode { get; set; } = "System";   // System | Light | Dark
    public string Accent { get; set; } = "Green";
    public string FontFamily { get; set; } = "Inter";
    public double FontScale { get; set; } = 1.0;

    // Sources
    public string SteamProfile { get; set; } = "";
    public string Country { get; set; } = "DE";
    public string GeizhalsDomain { get; set; } = "geizhals.de";
    public string ItadApiKey { get; set; } = "";
    public string GgDealsApiKey { get; set; } = "";
    public bool TrackKeys { get; set; } = true;

    // Tracking
    public int RefreshHours { get; set; } = 6;
    public int ItemDealPercent { get; set; } = 15;     // item deal: new price this % under its usual (median) price
    public int GameDealDiscount { get; set; } = 60;    // game deal: Steam discount at least this %
    public int KeyDealPercent { get; set; } = 50;      // game deal: key at least this % under Steam's regular price

    // Window
    public double WinW { get; set; } = 1180;
    public double WinH { get; set; } = 760;
    public int WinX { get; set; } = -1;
    public int WinY { get; set; } = -1;
    public string LastPage { get; set; } = "Games";
}
