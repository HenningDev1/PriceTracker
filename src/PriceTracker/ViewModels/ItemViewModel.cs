using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PriceTracker.Models;
using PriceTracker.Services;

namespace PriceTracker.ViewModels;

public sealed partial class ItemViewModel : ObservableObject
{
    private readonly Tracker _tracker;
    public ItemEntry Entry { get; }

    [ObservableProperty] private Bitmap? _image;

    public ItemViewModel(ItemEntry entry, Tracker tracker)
    {
        Entry = entry;
        _tracker = tracker;
        Deals = DealRules.ForItem(entry, tracker.Settings);
        History = DealRules.ItemHistory(entry).Where(p => p.Time >= DateTime.Now.AddYears(-2)).ToList();
        _ = LoadAsync();
    }

    private async Task LoadAsync() => Image = await ImageLoader.GetAsync(Entry.ImageUrl, 300);

    public string Url => Entry.Url;
    public string? Folder => Entry.Folder;
    public bool InFolder => Entry.Folder != null;
    /// Shown indented under its open folder in the list.
    public bool IsChild { get; init; }
    public Avalonia.Thickness RowIndent => IsChild ? new Avalonia.Thickness(28, 0, 0, 0) : default;
    public List<string> OtherFolders => _tracker.Folders.Where(f => f != Entry.Folder).ToList();
    public bool HasOtherFolders => OtherFolders.Count > 0;

    [ObservableProperty] private string _folderInput = "";

    [RelayCommand] private void MoveToNewFolder() { if (!string.IsNullOrWhiteSpace(FolderInput)) _tracker.MoveToFolder(Entry, FolderInput); }
    [RelayCommand] private void MoveTo(string folder) => _tracker.MoveToFolder(Entry, folder);
    [RelayCommand] private void RemoveFromFolder() => _tracker.MoveToFolder(Entry, null);
    public string Name => Entry.Name;
    public List<DealInfo> Deals { get; }
    public bool IsDeal => Deals.Count > 0;
    public string? DealBadge => Deals.OrderByDescending(d => d.Score).FirstOrDefault()?.Badge;
    public string DealReasons => string.Join("\n", Deals.Select(d => d.Reason));

    public string NewPriceText => Format.Money(Entry.NewPrice);
    public string NewShopText => Entry.NewShop != null
        ? $"{Entry.NewShop} · {Entry.OfferCount} offers"
        : Entry.OfferCount > 0 ? $"{Entry.OfferCount} offers" : "Geizhals";
    public bool HasUsed => Entry.UsedPrice.HasValue;
    public string UsedPriceText => Entry.UsedPrice.HasValue ? Format.Money(Entry.UsedPrice) : "—";
    public string UsedLabelText => Entry.UsedLabel ?? "None listed";

    public string UsualText => DealRules.UsualPrice(Entry) is { } u ? Format.Money(u) : "collecting…";
    public string LowestText => DealRules.ItemLowest(Entry) is { } l ? Format.Money(l) : "—";
    public string HighestText => DealRules.ItemHighest(Entry) is { } h ? Format.Money(h) : "—";
    public bool HasGeizhalsHistory => Entry.GeizhalsHistory.Count > 0;
    public string HistorySourceText => HasGeizhalsHistory
        ? $"Geizhals price history since {Entry.GeizhalsHistory[0].Time:MMM yyyy} + recorded by Price Tracker (last 2 years shown)"
        : "Recorded by Price Tracker";

    public string TrendText
    {
        get
        {
            var pts = DealRules.ItemHistory(Entry).Where(p => p.A.HasValue).ToList();
            if (pts.Count < 2) return "";
            var first = pts.First(p => p.Time >= DateTime.Now.AddDays(-30) || p == pts[^1]).A!.Value;
            var now = pts[^1].A!.Value;
            if (first == 0) return "";
            var pct = (double)((now - first) / first) * 100;
            return Math.Abs(pct) < 0.5 ? "stable (30 days)" : $"{(pct < 0 ? "▼" : "▲")} {Math.Abs(pct):0.#}% (30 days)";
        }
    }
    public bool TrendDown => TrendText.StartsWith('▼');

    public string CheckedText => Entry.LastError ?? $"Checked {Format.Ago(Entry.LastChecked)}";
    public bool HasError => Entry.LastError != null;
    public List<PricePoint> History { get; } = new();

    [RelayCommand] private void OpenGeizhals() => Links.Open(Entry.Url);
    [RelayCommand] private void OpenOffers() => Links.Open(Entry.Url.Split('#')[0] + "#offerlist");
    [RelayCommand] private void OpenUsed() => Links.Open(Entry.UsedUrl ?? Entry.Url);
    [RelayCommand] private void Remove() => _tracker.RemoveItem(Entry);
}
