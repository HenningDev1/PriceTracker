using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PriceTracker.Services;
using PriceTracker.ViewModels;
using PriceTracker.Views;

namespace PriceTracker;

public partial class App : Application
{
    public static MainViewModel? Main { get; private set; }
    public static Tracker? Tracker { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var screenshot = Environment.GetEnvironmentVariable("PRICETRACKER_SCREENSHOT");
            if (Environment.GetEnvironmentVariable("PRICETRACKER_DEMO") == "1") DemoData.Write();

            var settings = Storage.LoadSettings();
            var data = Storage.LoadData();
            ThemeService.Apply(settings);

            Tracker = new Tracker(data, settings)
            {
                AutoRefreshEnabled = screenshot == null && Environment.GetEnvironmentVariable("PRICETRACKER_SELFTEST") == null,
            };
            Main = new MainViewModel(Tracker);
            var window = new MainWindow { DataContext = Main };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => Tracker.Save();
            Tracker.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
