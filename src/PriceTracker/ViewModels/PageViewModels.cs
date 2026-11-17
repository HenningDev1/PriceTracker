using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PriceTracker.Services;

namespace PriceTracker.ViewModels;

public abstract partial class PageViewModel : ObservableObject
{
    protected readonly Tracker Tracker;
    protected PageViewModel(Tracker tracker) => Tracker = tracker;
    public abstract string Title { get; }
    public abstract void Reload();
}

// ───────────────────────────── Games ─────────────────────────────

public sealed partial class SteamHitViewModel : ObservableObject
{
    public SteamSearchHit Hit { get; }
    [ObservableProperty] private Bitmap? _image;
    public SteamHitViewModel(SteamSearchHit hit) { Hit = hit; _ = Load(); }
    private async Task Load() => Image = await ImageLoader.GetAsync(Hit.Image, 240);
    public string Name => Hit.Name;
    public string PriceText => Hit.Price.HasValue ? Format.Money(Hit.Price) : "";
}

public sealed partial class GamesPageViewModel : PageViewModel
{
    public override string Title => "Games";
    public ObservableCollection<GameViewModel> Games { get; } = new();
    public ObservableCollection<SteamHitViewModel> SearchResults { get; } = new();
    public string[] SortOptions { get; } = { "Best deal", "Name", "Price", "Discount", "Key savings", "Recently added" };

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _sort = "Best deal";
    [ObservableProperty] private GameViewModel? _selected;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _subtitle = "";

    public bool HasProfile => !string.IsNullOrWhiteSpace(Tracker.Settings.SteamProfile);
    public bool IsEmpty => Games.Count == 0 && string.IsNullOrEmpty(Filter);

    public GamesPageViewModel(Tracker tracker) : base(tracker) { }

    partial void OnFilterChanged(string value) => Reload();
    partial void OnSortChanged(string value) => Reload();

    public override void Reload()
    {
        var keep = Selected?.AppId;
        var all = Tracker.Data.Games.Select(g => new GameViewModel(g, Tracker));
        if (!string.IsNullOrWhiteSpace(Filter))
            all = all.Where(g => g.Name.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase));
        all = Sort switch
        {
            "Name" => all.OrderBy(g => g.Name),
            "Price" => all.OrderBy(g => g.BestPrice ?? decimal.MaxValue),
            "Discount" => all.OrderByDescending(g => g.Entry.Discount).ThenBy(g => g.Name),
            "Key savings" => all.OrderByDescending(g => (g.Entry.SteamPrice ?? 0) - (g.Entry.KeyPrice ?? g.Entry.SteamPrice ?? 0)),
            "Recently added" => all.OrderByDescending(g => g.Entry.Added),
            _ => all.OrderByDescending(g => g.Deals.Select(d => d.Score).DefaultIfEmpty(0).Max()).ThenByDescending(g => g.Entry.Discount).ThenBy(g => g.Name),
        };
        Games.Clear();
        foreach (var g in all) Games.Add(g);
        Selected = Games.FirstOrDefault(g => g.AppId == keep) ?? Games.FirstOrDefault();

        var total = Tracker.Data.Games.Count;
        var onSale = Tracker.Data.Games.Count(g => g.Discount > 0);
        Subtitle = total == 0 ? "Import your Steam wishlist or add games manually"
            : $"{total} game{(total == 1 ? "" : "s")} · {onSale} on sale";
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasProfile));
    }

    [RelayCommand] private Task ImportWishlist() => Tracker.SyncWishlistAsync();
    [RelayCommand] private Task Refresh() => Tracker.RefreshGamesAsync();

    [RelayCommand] private void ToggleSearch() => IsSearchOpen = !IsSearchOpen;
    [RelayCommand] private void GoToSettings() => App.Main?.Navigate("Settings");

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        IsSearching = true;
        try
        {
            var hits = await SteamService.SearchAsync(SearchText.Trim(), Tracker.Settings.Country);
            SearchResults.Clear();
            foreach (var h in hits) SearchResults.Add(new SteamHitViewModel(h));
        }
        catch (Exception ex) { App.Main?.SetStatus("Steam search failed: " + ex.Message); }
        finally { IsSearching = false; }
    }

    [RelayCommand]
    private async Task Add(SteamHitViewModel hit)
    {
        IsSearchOpen = false;
        SearchResults.Clear();
        SearchText = "";
        await Tracker.AddGameAsync(hit.Hit.AppId, hit.Hit.Name);
    }
}

// ───────────────────────────── Items ─────────────────────────────

public sealed partial class ItemHitViewModel : ObservableObject
{
    public GhSearchHit Hit { get; }
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _added;
    public ItemHitViewModel(GhSearchHit hit, bool added) { Hit = hit; _added = added; _ = Load(); }
    private async Task Load() => Image = await ImageLoader.GetAsync(Hit.Image, 200);
    public string Name => Hit.Name;
    public string PriceText => Hit.Price.HasValue ? "from " + Format.Money(Hit.Price) : "no offers";
    public string OffersText => (Hit.Variants ? "Several variants · " : "") + (Hit.Offers ?? "");
    [RelayCommand] private void Open() => Links.Open(Hit.Url);
}

public sealed partial class ItemsPageViewModel : PageViewModel
{
    public override string Title => "Wishlist";
    /// Loose items plus folders (and, under an open folder, its variants).
    public ObservableCollection<object> Rows { get; } = new();
    public ObservableCollection<ItemHitViewModel> SearchResults { get; } = new();
    public ObservableCollection<string> Folders { get; } = new();

    private List<ItemViewModel> _items = new();
    private List<FolderViewModel> _folders = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _showResults;
    [ObservableProperty] private object? _selected;
    [ObservableProperty] private string _subtitle = "";
    /// New items from the search results go into this folder (empty = no folder).
    [ObservableProperty] private string _targetFolder = "";

    public bool IsEmpty => _items.Count == 0;
    public bool HasFolders => Folders.Count > 0;

    public ItemsPageViewModel(Tracker tracker) : base(tracker) { }

    public override void Reload()
    {
        var keep = Key(Selected);
        _items = Tracker.Data.Items.Select(i => new ItemViewModel(i, Tracker)).ToList();
        _folders = _items.Where(i => i.InFolder).GroupBy(i => i.Folder!)
            .Select(g => new FolderViewModel(g.Key, g.OrderBy(i => i.Entry.NewPrice ?? decimal.MaxValue).ToList(), Tracker, Relayout))
            .OrderByDescending(f => f.IsDeal).ThenBy(f => f.Name).ToList();
        Folders.Clear();
        foreach (var f in _folders) Folders.Add(f.Name);
        Relayout();
        Selected = Rows.FirstOrDefault(r => Key(r) == keep)
                   ?? (object?)_items.FirstOrDefault(i => Key(i) == keep)
                   ?? Rows.FirstOrDefault();
        foreach (var r in SearchResults) r.Added = Tracker.Data.Items.Any(x => x.Url == r.Hit.Url);

        var deals = _items.Count(i => i.IsDeal);
        Subtitle = _items.Count == 0 ? "Search Geizhals and add items to track their prices"
            : $"{_items.Count} item{(_items.Count == 1 ? "" : "s")}" + (_folders.Count > 0 ? $" · {_folders.Count} folder{(_folders.Count == 1 ? "" : "s")}" : "")
              + $" · prices from {Tracker.Settings.GeizhalsDomain}" + (deals > 0 ? $" · {deals} deal{(deals == 1 ? "" : "s")}" : "");
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasFolders));
    }

    /// Rebuilds the visible rows (e.g. after opening/closing a folder) without reloading anything.
    private void Relayout()
    {
        var sel = Selected;
        Rows.Clear();
        foreach (var f in _folders)
        {
            Rows.Add(f);
            if (f.IsExpanded)
                foreach (var m in f.Members) Rows.Add(new ItemViewModel(m.Entry, Tracker) { IsChild = true });
        }
        foreach (var i in _items.Where(i => !i.InFolder).OrderByDescending(i => i.IsDeal).ThenBy(i => i.Name)) Rows.Add(i);
        foreach (var f in _folders) f.NotifyExpanded();
        if (sel != null) Selected = Rows.FirstOrDefault(r => Key(r) == Key(sel)) ?? sel;
    }

    public void SelectRow(ItemViewModel item)
    {
        var folder = _folders.FirstOrDefault(f => f.Name == item.Folder);
        if (folder is { IsExpanded: false }) folder.ToggleCommand.Execute(null);
        Selected = Rows.FirstOrDefault(r => r is ItemViewModel i && i.Url == item.Url) ?? item;
    }

    private static string? Key(object? row) => row switch
    {
        ItemViewModel i => "i:" + i.Url,
        FolderViewModel f => "f:" + f.Name,
        _ => null,
    };

    [RelayCommand] private void UseFolder(string name) => TargetFolder = name;

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || IsSearching) return;
        IsSearching = true;
        ShowResults = true;
        SearchResults.Clear();
        try
        {
            var hits = await Tracker.SearchItemsAsync(SearchText.Trim());
            foreach (var h in hits)
                SearchResults.Add(new ItemHitViewModel(h, Tracker.Data.Items.Any(x => x.Url == h.Url)));
        }
        catch (Exception ex) { App.Main?.SetStatus("Geizhals search failed: " + ex.Message); }
        finally { IsSearching = false; }
    }

    [RelayCommand] private void CloseResults() { ShowResults = false; }

    [RelayCommand]
    private async Task Add(ItemHitViewModel hit)
    {
        hit.Added = true;
        await Tracker.AddItemAsync(hit.Hit, TargetFolder);
    }

    [RelayCommand] private Task Refresh() => Tracker.RefreshItemsAsync();
}

// ───────────────────────────── Deals ─────────────────────────────

public sealed partial class DealViewModel : ObservableObject
{
    [ObservableProperty] private Bitmap? _image;
    public required string Name { get; init; }
    public required string Badge { get; init; }
    public required string Reason { get; init; }
    public required string PriceText { get; init; }
    public string? OldPriceText { get; init; }
    public required string Kind { get; init; }       // "Game" | "Item"
    public required string PrimaryLabel { get; init; }
    public required string PrimaryUrl { get; init; }
    public string? SecondaryLabel { get; init; }
    public string? SecondaryUrl { get; init; }
    public bool HasSecondary => SecondaryUrl != null;
    public bool HasOldPrice => OldPriceText != null;
    public bool IsAllTimeLow { get; init; }
    public double Score { get; init; }
    public bool Wide { get; init; }

    public async Task LoadAsync(string? url, int width) => Image = await ImageLoader.GetAsync(url, width);

    [RelayCommand] private void OpenPrimary() => Links.Open(PrimaryUrl);
    [RelayCommand] private void OpenSecondary() => Links.Open(SecondaryUrl);
}

public sealed partial class DealsPageViewModel : PageViewModel
{
    public override string Title => "Deals";
    public ObservableCollection<DealViewModel> Deals { get; } = new();
    public string[] Filters { get; } = { "All", "Games", "Items" };
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private int _count;

    public string RulesText =>
        $"Games: all-time low on Steam (needs GG.deals/IsThereAnyDeal or {DealRules.OwnHistoryDays} days of history), Steam discount ≥ {Tracker.Settings.GameDealDiscount}%, or a key ≥ {Tracker.Settings.KeyDealPercent}% under Steam's regular price.\n" +
        $"Items: new price ≥ {Tracker.Settings.ItemDealPercent}% under its usual (90-day median) price, a new all-time low, or a used offer ≥ {DealRules.UsedBargainPercent}% under new.";

    public bool IsEmpty => Deals.Count == 0;

    public DealsPageViewModel(Tracker tracker) : base(tracker) { }

    partial void OnFilterChanged(string value) => Reload();

    public override void Reload()
    {
        var list = new List<DealViewModel>();
        var s = Tracker.Settings;
        foreach (var g in Tracker.Data.Games)
        {
            var deals = DealRules.ForGame(g, s);
            if (deals.Count == 0) continue;
            var top = deals.OrderByDescending(d => d.Score).First();
            bool keyDeal = top.Kind == DealKind.CheapKey;
            var vm = new DealViewModel
            {
                Name = g.Name,
                Badge = top.Badge,
                Reason = string.Join("\n", deals.Select(d => d.Reason)),
                PriceText = Format.Money(keyDeal ? g.KeyPrice : g.SteamPrice),
                OldPriceText = (keyDeal || g.Discount > 0) ? Format.Money(g.SteamRegular) : null,
                Kind = "Game",
                PrimaryLabel = keyDeal ? "View keys" : "Steam",
                PrimaryUrl = keyDeal ? (g.KeySource == "GG.deals" ? g.GgDealsUrl : g.KeyPageUrl) ?? SteamService.StoreUrl(g.AppId) : SteamService.StoreUrl(g.AppId),
                SecondaryLabel = keyDeal ? "Steam" : "SteamDB",
                SecondaryUrl = keyDeal ? SteamService.StoreUrl(g.AppId) : SteamService.SteamDbUrl(g.AppId),
                IsAllTimeLow = deals.Any(d => d.Kind == DealKind.AllTimeLow) || deals.Any(d => d.Badge.Contains("all-time", StringComparison.OrdinalIgnoreCase)),
                Score = top.Score,
                Wide = true,
            };
            _ = vm.LoadAsync(g.CoverUrl, 460);
            list.Add(vm);
        }
        foreach (var i in Tracker.Data.Items)
        {
            var deals = DealRules.ForItem(i, s);
            if (deals.Count == 0) continue;
            var top = deals.OrderByDescending(d => d.Score).First();
            bool used = top.Kind == DealKind.UsedBargain;
            var vm = new DealViewModel
            {
                Name = i.Name,
                Badge = top.Badge,
                Reason = string.Join("\n", deals.Select(d => d.Reason)),
                PriceText = Format.Money(used ? i.UsedPrice : i.NewPrice),
                OldPriceText = used ? Format.Money(i.NewPrice) : DealRules.UsualPrice(i) is { } u ? Format.Money(u) : null,
                Kind = "Item",
                PrimaryLabel = used ? "Used offer" : "Geizhals",
                PrimaryUrl = used ? i.UsedUrl ?? i.Url : i.Url,
                SecondaryLabel = used ? "Geizhals" : null,
                SecondaryUrl = used ? i.Url : null,
                IsAllTimeLow = top.Kind == DealKind.AllTimeLow,
                Score = top.Score,
            };
            _ = vm.LoadAsync(i.ImageUrl, 300);
            list.Add(vm);
        }

        Count = list.Count;
        if (Filter == "Games") list = list.Where(d => d.Kind == "Game").ToList();
        if (Filter == "Items") list = list.Where(d => d.Kind == "Item").ToList();

        Deals.Clear();
        foreach (var d in list.OrderByDescending(d => d.IsAllTimeLow).ThenByDescending(d => d.Score)) Deals.Add(d);
        Subtitle = Count == 0 ? "Nothing unusually cheap right now" : $"{Count} deal{(Count == 1 ? "" : "s")} right now";
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(RulesText));
    }
}
