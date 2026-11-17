using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PriceTracker.Models;
using PriceTracker.Services;

namespace PriceTracker.ViewModels;

public sealed partial class GameViewModel : ObservableObject
{
    private readonly Tracker _tracker;
    public GameEntry Entry { get; }

    [ObservableProperty] private Bitmap? _cover;

    public GameViewModel(GameEntry entry, Tracker tracker)
    {
        Entry = entry;
        _tracker = tracker;
        Deals = DealRules.ForGame(entry, tracker.Settings);
        History = entry.History.ToList();
        _ = LoadCoverAsync();
    }

    private async Task LoadCoverAsync() => Cover = await ImageLoader.GetAsync(Entry.CoverUrl);

    public int AppId => Entry.AppId;
    public string Name => Entry.Name;
    public List<DealInfo> Deals { get; }
    public bool IsDeal => Deals.Count > 0;
    public string? DealBadge => Deals.OrderByDescending(d => d.Score).FirstOrDefault()?.Badge;
    public string DealReasons => string.Join("\n", Deals.Select(d => d.Reason));

    public string SteamPriceText => Entry.IsFree ? "Free" : Entry.Unreleased && Entry.SteamPrice == null ? "Not released" : Format.Money(Entry.SteamPrice);
    public bool HasDiscount => Entry.Discount > 0;
    public string DiscountText => $"-{Entry.Discount}%";
    public string RegularText => Format.Money(Entry.SteamRegular);

    public bool HasKey => Entry.KeyPrice.HasValue;
    public string KeyPriceText => Entry.KeyPrice.HasValue ? Format.Money(Entry.KeyPrice)
        : KeysUnavailable ? "Unavailable"
        : Entry.KeyPageUrl == null && Entry.KeySource == null ? "No keys found" : "—";
    /// The key source couldn't be reached – that's not the same as "this game has no keys".
    public bool KeysUnavailable => Entry.KeyPrice == null && Entry.LastError?.StartsWith("Keys:") == true;
    public string KeyShopText => KeysUnavailable ? (string.IsNullOrEmpty(_tracker.Settings.GgDealsApiKey) ? "Add GG.deals key in Settings" : "Source unreachable")
        : Entry.KeyShop == null
        ? (Entry.KeyOffers > 0 ? $"{Entry.KeyOffers} offers" : Entry.KeySource ?? "Key shops")
        : Entry.KeyOffers > 0 ? $"{Entry.KeyShop} · {Entry.KeyOffers} offers" : Entry.KeyShop;

    public bool KeyIsCheaper => Entry.KeyPrice.HasValue && Entry.SteamPrice.HasValue && Entry.KeyPrice < Entry.SteamPrice;
    public string SavingsText
    {
        get
        {
            if (!KeyIsCheaper) return "";
            var save = Entry.SteamPrice!.Value - Entry.KeyPrice!.Value;
            return $"Save {Format.Money(save)} with a key";
        }
    }

    public decimal? BestPrice => new[] { Entry.SteamPrice, Entry.KeyPrice }.Where(p => p.HasValue).DefaultIfEmpty().Min();
    public string AtlText => DealRules.SteamAllTimeLow(Entry) is { } l ? Format.Money(l) : "—";
    public string KeyAtlText => DealRules.KeyAllTimeLow(Entry) is { } l ? Format.Money(l) : "—";
    public string AtlSource =>
        !string.IsNullOrEmpty(_tracker.Settings.ItadApiKey) ? "from IsThereAnyDeal + recorded"
        : Entry.SteamHistoricLow.HasValue ? "from GG.deals + recorded"
        : $"recorded by Price Tracker (trusted after {DealRules.OwnHistoryDays} days – add a GG.deals key for real all-time lows)";
    public bool HasGgDeals => Entry.GgDealsUrl != null;

    public string CheckedText => Entry.LastError ?? $"Checked {Format.Ago(Entry.LastChecked)}";
    public bool HasError => Entry.LastError != null;
    public string SourceText => Entry.FromWishlist ? "Steam wishlist" : "Added manually";

    public List<PricePoint> History { get; } = new();

    [RelayCommand] private void OpenSteam() => Links.Open(SteamService.StoreUrl(Entry.AppId));
    [RelayCommand] private void OpenSteamDb() => Links.Open(SteamService.SteamDbUrl(Entry.AppId));
    [RelayCommand]
    private void OpenGgDeals() => Links.Open(Entry.GgDealsUrl);
    [RelayCommand]
    private void OpenKeys() => Links.Open(Entry.KeySource == "GG.deals" && Entry.GgDealsUrl != null ? Entry.GgDealsUrl : Entry.KeyPageUrl ??
        $"https://www.keyforsteam.de/?s={Uri.EscapeDataString(Entry.Name)}");
    [RelayCommand] private void Remove() => _tracker.RemoveGame(Entry);
}
