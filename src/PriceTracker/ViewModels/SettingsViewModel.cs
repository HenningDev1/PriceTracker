using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PriceTracker.Services;

namespace PriceTracker.ViewModels;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    public override string Title => "Settings";
    private bool _loading;

    public AccentOption[] Accents => ThemeService.Accents;
    public string[] Modes => ThemeService.Modes;
    public List<string> Fonts { get; } = ThemeService.AvailableFonts();
    public string[] Countries { get; } = { "DE", "AT", "FR", "NL", "BE", "ES", "IT", "FI", "IE", "PT", "LU" };
    public string[] GeizhalsDomains { get; } = { "geizhals.de", "geizhals.at", "geizhals.eu" };
    public int[] RefreshOptions { get; } = { 1, 3, 6, 12, 24 };

    [ObservableProperty] private string _themeMode = "System";
    [ObservableProperty] private AccentOption _accent = ThemeService.Accents[0];
    [ObservableProperty] private string _fontFamily = "Inter";
    [ObservableProperty] private double _fontScale = 1.0;

    [ObservableProperty] private string _steamProfile = "";
    [ObservableProperty] private string _country = "DE";
    [ObservableProperty] private bool _trackKeys = true;
    [ObservableProperty] private string _itadApiKey = "";
    [ObservableProperty] private string _geizhalsDomain = "geizhals.de";

    [ObservableProperty] private int _refreshHours = 6;
    [ObservableProperty] private double _itemDealPercent = 15;
    [ObservableProperty] private double _gameDealDiscount = 60;
    [ObservableProperty] private double _keyDealPercent = 50;

    public string DataFolder => Storage.DataDir;
    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public bool WebViewOk => BrowserFetcher.IsAvailable;

    public SettingsPageViewModel(Tracker tracker) : base(tracker) => Reload();

    public override void Reload()
    {
        _loading = true;
        var s = Tracker.Settings;
        ThemeMode = s.ThemeMode;
        Accent = ThemeService.Accents.FirstOrDefault(a => a.Name == s.Accent) ?? ThemeService.Accents[0];
        FontFamily = Fonts.Contains(s.FontFamily) ? s.FontFamily : "Inter";
        FontScale = s.FontScale;
        SteamProfile = s.SteamProfile;
        Country = s.Country;
        TrackKeys = s.TrackKeys;
        ItadApiKey = s.ItadApiKey;
        GeizhalsDomain = s.GeizhalsDomain;
        RefreshHours = s.RefreshHours;
        ItemDealPercent = s.ItemDealPercent;
        GameDealDiscount = s.GameDealDiscount;
        KeyDealPercent = s.KeyDealPercent;
        _loading = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading) return;
        var s = Tracker.Settings;
        s.ThemeMode = ThemeMode;
        s.Accent = Accent.Name;
        s.FontFamily = FontFamily;
        s.FontScale = Math.Round(FontScale, 2);
        s.SteamProfile = SteamProfile.Trim();
        s.Country = Country;
        s.TrackKeys = TrackKeys;
        s.ItadApiKey = ItadApiKey.Trim();
        s.GeizhalsDomain = GeizhalsDomain;
        s.RefreshHours = RefreshHours;
        s.ItemDealPercent = (int)Math.Round(ItemDealPercent);
        s.GameDealDiscount = (int)Math.Round(GameDealDiscount);
        s.KeyDealPercent = (int)Math.Round(KeyDealPercent);
        Storage.SaveSettings(s);

        if (e.PropertyName is nameof(ThemeMode) or nameof(Accent) or nameof(FontFamily) or nameof(FontScale))
            ThemeService.Apply(s);
        if (e.PropertyName is nameof(ItemDealPercent) or nameof(GameDealDiscount) or nameof(KeyDealPercent))
            App.Main?.ReloadPages();
    }

    [RelayCommand] private void SetMode(string mode) => ThemeMode = mode;
    [RelayCommand] private void SetAccent(AccentOption a) => Accent = a;
    [RelayCommand] private void OpenDataFolder() => Links.Open(new Uri(Storage.DataDir).AbsoluteUri);
    [RelayCommand] private void OpenItad() => Links.Open("https://isthereanydeal.com/apps/my/");
    [RelayCommand] private void OpenRepo() => Links.Open("https://github.com/HenningDev1/PriceTracker");
    [RelayCommand] private Task ImportWishlist() => Tracker.SyncWishlistAsync();

    [RelayCommand]
    private void ResetAppearance()
    {
        FontFamily = "Inter";
        FontScale = 1.0;
        Accent = ThemeService.Accents[0];
        ThemeMode = "System";
    }
}
