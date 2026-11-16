using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using PriceTracker.Services;

namespace PriceTracker.ViewModels;

public sealed partial class NavItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }   // path data
    [ObservableProperty] private int _badge;
    public bool HasBadge => Badge > 0;
    partial void OnBadgeChanged(int value) => OnPropertyChanged(nameof(HasBadge));
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Tracker _tracker;
    private readonly DispatcherTimer _clock;

    public GamesPageViewModel Games { get; }
    public ItemsPageViewModel Items { get; }
    public DealsPageViewModel Deals { get; }
    public SettingsPageViewModel Settings { get; }

    public ObservableCollection<NavItem> Nav { get; }

    [ObservableProperty] private NavItem _selectedNav;
    [ObservableProperty] private PageViewModel _current;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _lastRefreshText = "";

    public MainViewModel(Tracker tracker)
    {
        _tracker = tracker;
        Games = new GamesPageViewModel(tracker);
        Items = new ItemsPageViewModel(tracker);
        Deals = new DealsPageViewModel(tracker);
        Settings = new SettingsPageViewModel(tracker);

        Nav = new ObservableCollection<NavItem>
        {
            new() { Key = "Games", Label = "Games", Icon = Icons.Gamepad },
            new() { Key = "Items", Label = "Wishlist", Icon = Icons.Bag },
            new() { Key = "Deals", Label = "Deals", Icon = Icons.Flame },
            new() { Key = "Settings", Label = "Settings", Icon = Icons.Gear },
        };

        tracker.DataChanged += () => Dispatcher.UIThread.Post(ReloadPages);
        tracker.StatusChanged += s => Dispatcher.UIThread.Post(() => Status = s);
        tracker.BusyChanged += b => Dispatcher.UIThread.Post(() => IsBusy = b);

        ReloadPages();
        _selectedNav = Nav.FirstOrDefault(n => n.Key == tracker.Settings.LastPage) ?? Nav[0];
        _current = PageFor(_selectedNav.Key);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => UpdateLastRefresh();
        _clock.Start();
    }

    public void SetStatus(string s) => Status = s;

    public void ReloadPages()
    {
        Games.Reload();
        Items.Reload();
        Deals.Reload();
        Nav.First(n => n.Key == "Deals").Badge = Deals.Count;
        UpdateLastRefresh();
    }

    private void UpdateLastRefresh() =>
        LastRefreshText = _tracker.Data.LastRefresh == null ? "Not updated yet" : $"Updated {Format.Ago(_tracker.Data.LastRefresh)}";

    private PageViewModel PageFor(string key) => key switch
    {
        "Items" => Items,
        "Deals" => Deals,
        "Settings" => Settings,
        _ => Games,
    };

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value == null) return;
        Current = PageFor(value.Key);
        _tracker.Settings.LastPage = value.Key;
        Storage.SaveSettings(_tracker.Settings);
    }

    public void Navigate(string key) => SelectedNav = Nav.First(n => n.Key == key);

    [RelayCommand] private Task RefreshAll() => _tracker.RefreshAllAsync(syncWishlist: true);
}

public static class Icons
{
    public const string Gamepad = "M7.5 6h9a5.5 5.5 0 0 1 5.4 6.6l-.9 4.4a2.6 2.6 0 0 1-4.6 1.1L14.6 16H9.4l-1.8 2.1A2.6 2.6 0 0 1 3 17l-.9-4.4A5.5 5.5 0 0 1 7.5 6Z M8 9.5v4 M6 11.5h4 M15.5 10.5h.01 M17.5 12.5h.01";
    public const string Bag = "M6 7h12l1 13H5L6 7Z M9 7V6a3 3 0 0 1 6 0v1";
    public const string Flame = "M12 3c.5 3.5 5 5.5 5 10.5a5 5 0 0 1-10 0c0-2.2 1-3.6 2.2-4.8.2 1.7 1 2.8 2.3 3.3C11 9.5 11 6 12 3Z";
    public const string Gear = "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z";
    public const string Refresh = "M20 11a8 8 0 0 0-14.8-3.5M4 4v4h4 M4 13a8 8 0 0 0 14.8 3.5M20 20v-4h-4";
    public const string Search = "M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14Z M20 20l-4-4";
    public const string Plus = "M12 5v14 M5 12h14";
    public const string External = "M14 4h6v6 M20 4l-9 9 M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5";
    public const string Trash = "M4 7h16 M10 11v6 M14 11v6 M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12 M9 7V4h6v3";
    public const string Download = "M12 4v11 M7 10l5 5 5-5 M5 20h14";
    public const string Key = "M15.5 7.5a3.5 3.5 0 1 1-3.4 4.4L4 20v-3h3v-3h3l1.6-1.6A3.5 3.5 0 0 1 15.5 7.5Z";
    public const string Close = "M6 6l12 12 M18 6L6 18";
    public const string Tag = "M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9-9-9Z M7.5 7.5h.01";
}
