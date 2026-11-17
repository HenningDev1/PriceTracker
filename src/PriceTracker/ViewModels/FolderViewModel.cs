using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PriceTracker.Models;
using PriceTracker.Services;

namespace PriceTracker.ViewModels;

/// A wishlist folder: several variants of one product (colours, storage sizes, board partners…)
/// shown as one row with the cheapest price across all of them.
public sealed partial class FolderViewModel : ObservableObject
{
    private static readonly HashSet<string> Expanded = new();
    private readonly Tracker _tracker;
    private readonly Action _relayout;

    public string Name { get; }
    public List<ItemViewModel> Members { get; }

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _renameInput;

    public FolderViewModel(string name, List<ItemViewModel> members, Tracker tracker, Action relayout)
    {
        Name = name;
        Members = members;
        _tracker = tracker;
        _relayout = relayout;
        _renameInput = name;
        History = MergeHistory(members.Select(m => m.Entry).ToList());
        _ = LoadAsync();
    }

    private async Task LoadAsync() => Image = await ImageLoader.GetAsync(Cheapest?.Entry.ImageUrl ?? Members.FirstOrDefault()?.Entry.ImageUrl, 300);

    public bool IsExpanded => Expanded.Contains(Name);
    public string Chevron => IsExpanded ? "M6 9l6 6 6-6" : "M9 6l6 6-6 6";

    public ItemViewModel? Cheapest => Members.Where(m => m.Entry.NewPrice.HasValue).OrderBy(m => m.Entry.NewPrice).FirstOrDefault();
    private ItemViewModel? CheapestUsed => Members.Where(m => m.Entry.UsedPrice.HasValue).OrderBy(m => m.Entry.UsedPrice).FirstOrDefault();

    public string CountText => $"{Members.Count} variant{(Members.Count == 1 ? "" : "s")}";
    public string NewPriceText => Format.Money(Cheapest?.Entry.NewPrice);
    public string NewShopText => Cheapest == null ? "—" : $"cheapest: {ShortName(Cheapest.Name)}";
    public string UsedPriceText => Format.Money(CheapestUsed?.Entry.UsedPrice);
    public string UsedLabelText => CheapestUsed == null ? "None listed" : ShortName(CheapestUsed.Name);
    public string CheapestName => Cheapest?.Name ?? "—";
    public string CheapestUsedName => CheapestUsed?.Name ?? "No used offers";

    public bool IsDeal => Members.Any(m => m.IsDeal);
    public int DealCount => Members.Count(m => m.IsDeal);
    public string DealBadge => DealCount == 1 ? Members.First(m => m.IsDeal).DealBadge ?? "Deal" : $"{DealCount} deals";
    public string DealReasons => string.Join("\n", Members.Where(m => m.IsDeal).Select(m => $"{ShortName(m.Name)}: {m.DealReasons.Replace("\n", "; ")}"));

    public string LowestText => History.Where(p => p.A.HasValue).Select(p => p.A).DefaultIfEmpty().Min() is { } l ? Format.Money(l) : "—";
    public string HighestText => History.Where(p => p.A.HasValue).Select(p => p.A).DefaultIfEmpty().Max() is { } h ? Format.Money(h) : "—";
    public string RangeText => Members.Count(m => m.Entry.NewPrice.HasValue) < 2 ? "—"
        : $"{Format.Money(Members.Min(m => m.Entry.NewPrice))} – {Format.Money(Members.Max(m => m.Entry.NewPrice))}";

    /// Cheapest new / used price across all variants over time.
    public List<PricePoint> History { get; }

    /// The common words of the variant names are the folder's subject; showing only the rest keeps rows short.
    private string ShortName(string name)
    {
        if (Members.Count < 2) return name;
        var common = Members.Select(m => m.Name.Split(' ')).Aggregate((a, b) => a.TakeWhile((w, i) => i < b.Length && b[i] == w).ToArray());
        var words = name.Split(' ');
        var rest = string.Join(' ', words.Skip(common.Length)).Trim(' ', ',');
        return rest.Length >= 3 ? rest : name;
    }

    private static List<PricePoint> MergeHistory(List<ItemEntry> items)
    {
        var hist = items.Select(i => DealRules.ItemHistory(i).Where(p => p.Time >= DateTime.Now.AddYears(-2)).ToList()).ToList();
        var times = hist.SelectMany(h => h.Select(p => p.Time)).Distinct().OrderBy(t => t).ToList();
        var result = new List<PricePoint>();
        foreach (var t in times)
        {
            decimal? Min(Func<PricePoint, decimal?> sel) => hist
                .Select(h => h.LastOrDefault(p => p.Time <= t && sel(p).HasValue))
                .Where(p => p != null).Select(p => sel(p!)).Min();
            var point = new PricePoint { Time = t, A = Min(p => p.A), B = Min(p => p.B) };
            if (result.Count > 0 && result[^1].A == point.A && result[^1].B == point.B) continue;
            result.Add(point);
        }
        return result;
    }

    public void NotifyExpanded()
    {
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(Chevron));
    }

    [RelayCommand]
    private void Toggle()
    {
        if (!Expanded.Remove(Name)) Expanded.Add(Name);
        _relayout();
    }

    [RelayCommand]
    private void Rename()
    {
        if (string.IsNullOrWhiteSpace(RenameInput) || RenameInput.Trim() == Name) return;
        if (Expanded.Remove(Name)) Expanded.Add(RenameInput.Trim());
        _tracker.RenameFolder(Name, RenameInput);
    }

    [RelayCommand] private void Ungroup() => _tracker.Ungroup(Name);
    [RelayCommand] private void OpenCheapest() => Links.Open(Cheapest?.Url);
    [RelayCommand] private void Select(ItemViewModel item) => App.Main?.Items.SelectRow(item);
}
