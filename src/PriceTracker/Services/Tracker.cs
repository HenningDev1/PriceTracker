using Avalonia.Threading;
using PriceTracker.Models;

namespace PriceTracker.Services;

/// Owns the tracked data and keeps it fresh. All members are used from the UI thread.
public sealed class Tracker
{
    public AppData Data { get; }
    public AppSettings Settings { get; }

    public event Action? DataChanged;
    public event Action<string>? StatusChanged;
    public event Action<bool>? BusyChanged;

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set { _busy = value; BusyChanged?.Invoke(value); }
    }

    private readonly DispatcherTimer _timer;
    public bool AutoRefreshEnabled { get; set; } = true;

    public Tracker(AppData data, AppSettings settings)
    {
        Data = data;
        Settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += async (_, _) => await AutoRefreshAsync();
    }

    public void Start()
    {
        _timer.Start();
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(1500);
            await AutoRefreshAsync();
        });
    }

    private async Task AutoRefreshAsync()
    {
        if (!AutoRefreshEnabled || IsBusy) return;
        if (Data.Games.Count == 0 && Data.Items.Count == 0 && string.IsNullOrWhiteSpace(Settings.SteamProfile)) return;
        var due = Data.LastRefresh == null || DateTime.Now - Data.LastRefresh > TimeSpan.FromHours(Math.Max(1, Settings.RefreshHours));
        if (due) await RefreshAllAsync(syncWishlist: true);
    }

    public void Save() => Storage.SaveData(Data);

    private void Status(string s) => StatusChanged?.Invoke(s);

    // ---------- history ----------

    public static void Record(List<PricePoint> history, decimal? a, decimal? b)
    {
        if (a == null && b == null) return;
        var now = DateTime.Now;
        var last = history.Count > 0 ? history[^1] : null;
        if (last != null && last.A == a && last.B == b && now - last.Time < TimeSpan.FromHours(20)) return;
        history.Add(new PricePoint { Time = now, A = a, B = b });
    }

    // ---------- all ----------

    public async Task RefreshAllAsync(bool syncWishlist)
    {
        if (IsBusy) return;
        IsBusy = true;
        var errors = new List<string>();
        try
        {
            if (syncWishlist && !string.IsNullOrWhiteSpace(Settings.SteamProfile))
            {
                try { await SyncWishlistCoreAsync(); }
                catch (Exception ex) { errors.Add("Wishlist: " + ex.Message); }
            }
            try { await RefreshGamesCoreAsync(); }
            catch (Exception ex) { errors.Add("Games: " + ex.Message); }
            try { await RefreshItemsCoreAsync(); }
            catch (Exception ex) { errors.Add("Items: " + ex.Message); }
            Data.LastRefresh = DateTime.Now;
            Save();
        }
        finally
        {
            IsBusy = false;
            DataChanged?.Invoke();
            Status(errors.Count == 0 ? $"Updated {DateTime.Now:HH:mm}" : string.Join("  ·  ", errors));
        }
    }

    public async Task RunAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await work(); Save(); }
        catch (Exception ex) { Status(ex.Message); }
        finally
        {
            IsBusy = false;
            DataChanged?.Invoke();
        }
    }

    // ---------- games ----------

    public Task SyncWishlistAsync() => RunAsync(async () =>
    {
        int added = await SyncWishlistCoreAsync();
        await RefreshGamesCoreAsync();
        Status(added > 0 ? $"Imported {added} game(s) from your wishlist" : "Wishlist is up to date");
    });

    private async Task<int> SyncWishlistCoreAsync()
    {
        Status("Reading your Steam wishlist…");
        var id = await SteamService.ResolveSteamIdAsync(Settings.SteamProfile);
        var ids = await SteamService.GetWishlistAsync(id);
        var set = ids.ToHashSet();
        // Games that left the wishlist (bought or removed) stop being tracked; manually added games stay.
        Data.Games.RemoveAll(g => g.FromWishlist && !set.Contains(g.AppId));
        int added = 0;
        foreach (var appId in ids)
        {
            var existing = Data.Games.FirstOrDefault(g => g.AppId == appId);
            if (existing != null) { existing.FromWishlist = true; continue; }
            Data.Games.Add(new GameEntry { AppId = appId, Name = $"App {appId}", FromWishlist = true });
            added++;
        }
        return added;
    }

    public Task AddGameAsync(int appId, string name) => RunAsync(async () =>
    {
        if (Data.Games.Any(g => g.AppId == appId)) { Status($"{name} is already tracked"); return; }
        var game = new GameEntry { AppId = appId, Name = name };
        Data.Games.Add(game);
        await RefreshGamesCoreAsync(new[] { game });
        Status($"Tracking {game.Name}");
    });

    public void RemoveGame(GameEntry g)
    {
        Data.Games.Remove(g);
        Save();
        DataChanged?.Invoke();
    }

    public Task RefreshGamesAsync() => RunAsync(async () =>
    {
        await RefreshGamesCoreAsync();
        Status($"Games updated {DateTime.Now:HH:mm}");
    });

    private async Task RefreshGamesCoreAsync(IReadOnlyList<GameEntry>? only = null)
    {
        var games = (only ?? Data.Games).ToList();
        if (games.Count == 0) return;

        Status("Checking Steam prices…");
        var apps = (await SteamService.GetAppsAsync(games.Select(g => g.AppId).ToList(), Settings.Country))
            .ToDictionary(a => a.AppId);
        foreach (var g in games)
        {
            if (!apps.TryGetValue(g.AppId, out var a)) continue;
            g.Name = a.Name;
            g.CoverUrl = a.Cover ?? g.CoverUrl;
            g.SteamPrice = a.Price;
            g.SteamRegular = a.Regular;
            g.Discount = a.Discount;
            g.IsFree = a.IsFree;
            g.Unreleased = a.Unreleased;
        }
        DataChanged?.Invoke();

        if (!string.IsNullOrWhiteSpace(Settings.ItadApiKey))
        {
            Status("Looking up all-time lows…");
            try
            {
                var lows = await ItadService.GetSteamLowsAsync(Settings.ItadApiKey, games.Select(g => g.AppId), Settings.Country);
                foreach (var g in games)
                    if (lows.TryGetValue(g.AppId, out var low)) g.SteamHistoricLow = low;
            }
            catch { /* optional source */ }
        }

        // GG.deals (official API, one request for up to 100 games): key prices + real all-time lows.
        var gg = new Dictionary<int, GgPrices>();
        string? ggError = null;
        if (!string.IsNullOrWhiteSpace(Settings.GgDealsApiKey))
        {
            Status("Checking GG.deals…");
            try { gg = await GgDealsService.GetPricesAsync(Settings.GgDealsApiKey, games.Select(g => g.AppId).ToList(), Settings.Country); }
            catch (Exception ex) { ggError = ex.Message; Status(ex.Message); }
        }

        int i = 0;
        foreach (var g in games)
        {
            i++;
            g.LastError = ggError;
            bool keyFresh = false;
            if (gg.TryGetValue(g.AppId, out var ggp))
            {
                g.GgDealsUrl = ggp.Url;
                g.KeyHistoricLow = ggp.HistoricalKeyshops;
                // ITAD's low is Steam-specific; GG.deals' retail low covers all official stores – use it if ITAD has none.
                if (string.IsNullOrWhiteSpace(Settings.ItadApiKey)) g.SteamHistoricLow = ggp.HistoricalRetail;
                if (Settings.TrackKeys && ggp.CurrentKeyshops.HasValue)
                {
                    g.KeyPrice = ggp.CurrentKeyshops;
                    g.KeyShop = "Key shops via GG.deals";
                    g.KeyOffers = 0;
                    g.KeySource = "GG.deals";
                    keyFresh = true;
                }
            }
            if (Settings.TrackKeys && !g.IsFree && !keyFresh)
            {
                Status($"Checking key prices {i}/{games.Count} · {g.Name}");
                try
                {
                    g.KeyPageUrl ??= await KeyForSteamService.FindPageAsync(g.Name);
                    if (g.KeyPageUrl != null)
                    {
                        var offer = await KeyForSteamService.GetOffersAsync(g.KeyPageUrl);
                        g.KeyPrice = offer?.LowPrice;
                        g.KeyShop = offer?.Shop;
                        g.KeyOffers = offer?.Offers ?? 0;
                        g.KeySource = "KeyForSteam";
                        keyFresh = true;
                    }
                }
                catch (Exception ex) { g.LastError = "Keys: " + (ggError != null ? ggError + " — " : "") + ex.Message; }
            }
            g.LastChecked = DateTime.Now;
            // Only record key prices we actually just saw (a stale value would fake a flat line).
            Record(g.History, g.SteamPrice, keyFresh ? g.KeyPrice : null);
            if (i % 5 == 0) { Save(); DataChanged?.Invoke(); }
        }
    }

    // ---------- items ----------

    public Task AddItemAsync(GhSearchHit hit, string? folder = null) => RunAsync(async () =>
    {
        folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
        var existing = Data.Items.FirstOrDefault(x => x.Url == hit.Url);
        if (existing != null)
        {
            if (folder != null && existing.Folder != folder) { existing.Folder = folder; Status($"Moved {hit.Name} to {folder}"); }
            else Status($"{hit.Name} is already on your wishlist");
            return;
        }
        var item = new ItemEntry { Url = hit.Url, Name = hit.Name, ImageUrl = hit.Image, NewPrice = hit.Price, Folder = folder };
        Data.Items.Add(item);
        DataChanged?.Invoke();
        await RefreshItemsCoreAsync(new[] { item });
        Status($"Tracking {item.Name}");
    });

    // ---------- folders ----------

    public IEnumerable<string> Folders => Data.Items.Where(i => i.Folder != null).Select(i => i.Folder!).Distinct().OrderBy(f => f);

    public void MoveToFolder(ItemEntry item, string? folder)
    {
        item.Folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
        Save();
        DataChanged?.Invoke();
    }

    public void RenameFolder(string from, string to)
    {
        to = to.Trim();
        if (to.Length == 0 || to == from) return;
        foreach (var i in Data.Items.Where(i => i.Folder == from)) i.Folder = to;
        Save();
        DataChanged?.Invoke();
    }

    /// Dissolves a folder; its items stay on the wishlist.
    public void Ungroup(string folder)
    {
        foreach (var i in Data.Items.Where(i => i.Folder == folder)) i.Folder = null;
        Save();
        DataChanged?.Invoke();
    }

    public void RemoveItem(ItemEntry i)
    {
        Data.Items.Remove(i);
        Save();
        DataChanged?.Invoke();
    }

    public Task RefreshItemsAsync() => RunAsync(async () =>
    {
        await RefreshItemsCoreAsync();
        Status($"Items updated {DateTime.Now:HH:mm}");
    });

    private async Task RefreshItemsCoreAsync(IReadOnlyList<ItemEntry>? only = null)
    {
        var items = (only ?? Data.Items).ToList();
        if (items.Count == 0) return;
        if (!BrowserFetcher.IsAvailable)
            throw new InvalidOperationException("Geizhals needs the Microsoft Edge WebView2 runtime, which isn't installed.");
        int i = 0;
        foreach (var item in items)
        {
            i++;
            Status($"Checking Geizhals {i}/{items.Count} · {item.Name}");
            try
            {
                var p = await GeizhalsService.GetProductAsync(item.Url);
                if (p == null) throw new InvalidOperationException("No data");
                if (!string.IsNullOrWhiteSpace(p.Name)) item.Name = p.Name;
                item.ImageUrl = p.Image ?? item.ImageUrl;
                var best = p.Offers.Where(o => o.Price.HasValue).OrderBy(o => o.Price).FirstOrDefault();
                item.NewPrice = best?.Price ?? Http.ParsePrice(p.FromText);
                item.NewShop = best?.Shop;
                item.OfferCount = p.Offers.Count;
                item.UsedPrice = p.Used?.Price;
                item.UsedLabel = p.Used?.Label;
                item.UsedUrl = p.Used?.Url;
                item.LastError = null;
                Record(item.History, item.NewPrice, item.UsedPrice);
            }
            catch (Exception ex) { item.LastError = ex.Message; }
            item.LastChecked = DateTime.Now;
            DataChanged?.Invoke();
            if (i < items.Count) await Task.Delay(1500);
        }
    }

    public async Task<List<GhSearchHit>> SearchItemsAsync(string query)
    {
        if (!BrowserFetcher.IsAvailable)
            throw new InvalidOperationException("Geizhals needs the Microsoft Edge WebView2 runtime, which isn't installed.");
        Status($"Searching Geizhals for \"{query}\"…");
        var hits = await GeizhalsService.SearchAsync(Settings.GeizhalsDomain, query);
        Status(hits.Count == 0 ? "Nothing found" : $"{hits.Count} results");
        return hits;
    }
}
