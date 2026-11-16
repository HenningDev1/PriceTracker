using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PriceTrackerSetup;

public partial class SetupWindow : Window
{
    private enum Mode { Install, Uninstall, Busy, Done }
    private Mode _mode;
    private bool _installedOk;

    public SetupWindow()
    {
        InitializeComponent();

        CloseLight.PointerReleased += (_, _) => { if (_mode != Mode.Busy) Close(); };
        MinLight.PointerReleased += (_, _) => WindowState = WindowState.Minimized;
        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Avalonia.Controls.Shapes.Ellipse)
                BeginMoveDrag(e);
        };

        CancelButton.Click += (_, _) => Close();
        PrimaryButton.Click += async (_, _) => await PrimaryAsync();
        SecondaryLeft.Click += (_, _) => ShowUninstall();
        Closing += (_, e) => { if (_mode == Mode.Busy) e.Cancel = true; };

        LocationText.Text = "Installs for your account only (no admin rights) to\n%LOCALAPPDATA%\\Programs\\Price Tracker";
        ToolTip.SetTip(LocationText, SetupEngine.InstallDir);

        if (Program.UninstallMode) ShowUninstall();
        else ShowWelcome();

        if (Environment.GetEnvironmentVariable("PRICETRACKER_SETUP_SCREENSHOT") is { Length: > 0 } shot)
            Opened += async (_, _) => await ScreenshotAsync(shot);
    }

    private void ShowWelcome()
    {
        _mode = Mode.Install;
        PageWelcome.IsVisible = true;
        PageUninstall.IsVisible = false;
        if (SetupEngine.IsInstalled)
        {
            var v = SetupEngine.InstalledVersion ?? "an older version";
            WelcomeTitle.Text = "Update Price Tracker";
            WelcomeText.Text = $"Price Tracker {v} is already installed. You can reinstall / update it to version {SetupEngine.Version} – your wishlist and price history are kept – or remove it.";
            PrimaryButton.Content = "Update";
            SecondaryLeft.IsVisible = true;
            DesktopShortcut.IsChecked = SetupEngine.HasDesktopShortcut;
        }
        else
        {
            PrimaryButton.Content = "Install";
        }
        WindowTitle.Text = "Price Tracker Setup";
    }

    private void ShowUninstall()
    {
        _mode = Mode.Uninstall;
        PageWelcome.IsVisible = false;
        PageUninstall.IsVisible = true;
        SecondaryLeft.IsVisible = false;
        PrimaryButton.Content = "Uninstall";
        PrimaryButton.Classes.Remove("primary");
        PrimaryButton.Classes.Add("danger");
        WindowTitle.Text = "Price Tracker Uninstall";
    }

    private async Task PrimaryAsync()
    {
        switch (_mode)
        {
            case Mode.Done:
                if (_installedOk && LaunchAfter.IsChecked == true) SetupEngine.Launch();
                Close();
                return;
            case Mode.Install:
                await RunAsync(install: true);
                return;
            case Mode.Uninstall:
                await RunAsync(install: false);
                return;
        }
    }

    private async Task RunAsync(bool install)
    {
        bool desktop = DesktopShortcut.IsChecked == true;
        bool removeData = RemoveData.IsChecked == true;
        _mode = Mode.Busy;
        PageWelcome.IsVisible = PageUninstall.IsVisible = false;
        PageProgress.IsVisible = true;
        ProgressTitle.Text = install ? "Installing…" : "Uninstalling…";
        PrimaryButton.IsEnabled = CancelButton.IsEnabled = false;
        SecondaryLeft.IsVisible = false;

        var progress = new Progress<(double Value, string Text)>(p =>
        {
            Bar.Value = p.Value;
            ProgressText.Text = p.Text;
        });

        string? error = null;
        try
        {
            await Task.Run(() =>
            {
                if (install) SetupEngine.Install(desktop, progress);
                else SetupEngine.Uninstall(removeData, progress);
            });
        }
        catch (Exception ex) { error = ex.Message; }

        await Task.Delay(250);
        _mode = Mode.Done;
        _installedOk = install && error == null;
        PageProgress.IsVisible = false;
        PageDone.IsVisible = true;
        CancelButton.IsVisible = false;
        PrimaryButton.IsEnabled = true;
        PrimaryButton.Classes.Remove("danger");
        PrimaryButton.Classes.Add("primary");

        if (error != null)
        {
            DoneTitle.Text = "Something went wrong";
            DoneText.Text = error;
            PrimaryButton.Content = "Close";
        }
        else if (install)
        {
            DoneTitle.Text = "Price Tracker is installed";
            DoneText.Text = "Find it in the Start menu" + (desktop ? " or on your desktop" : "") +
                            ". To remove it later, use Settings › Apps › Installed apps, or run this setup again.";
            PrimaryButton.Content = LaunchAfter.IsChecked == true ? "Open Price Tracker" : "Done";
        }
        else
        {
            DoneTitle.Text = "Price Tracker was removed";
            DoneText.Text = removeData ? "All program files and your data were deleted." : "Your wishlist and price history were kept in case you reinstall.";
            PrimaryButton.Content = "Close";
        }
    }

    // Used by scripts\screenshots.ps1 for the README.
    private async Task ScreenshotAsync(string path)
    {
        await Task.Delay(800);
        Root.CornerRadius = new CornerRadius(12);
        Root.ClipToBounds = true;
        Background = null;
        await Task.Delay(200);
        var scale = RenderScaling;
        using var bmp = new RenderTargetBitmap(new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bmp.Render(Root);
        bmp.Save(path);
        Close();
    }
}
