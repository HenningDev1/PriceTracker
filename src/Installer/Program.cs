using Avalonia;

namespace PriceTrackerSetup;

internal static class Program
{
    public static bool UninstallMode { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        bool has(string a) => args.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase));
        UninstallMode = has("/uninstall") || has("--uninstall");

        if (has("/quiet") || has("--quiet"))
        {
            try
            {
                var silent = new Progress<(double, string)>();
                if (UninstallMode) SetupEngine.Uninstall(removeData: false, silent);
                else SetupEngine.Install(desktopShortcut: false, silent);
                return 0;
            }
            catch { return 1; }
        }

        AppBuilder.Configure<SetupApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }
}
