using Avalonia;

namespace PriceTracker;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool screenshot = Environment.GetEnvironmentVariable("PRICETRACKER_SCREENSHOT") != null
                          || Environment.GetEnvironmentVariable("PRICETRACKER_SELFTEST") != null;
        using var mutex = new Mutex(true, "HenningDev.PriceTracker.SingleInstance", out bool isNew);
        if (!isNew && !screenshot)
        {
            Services.SingleInstance.ActivateExisting();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
