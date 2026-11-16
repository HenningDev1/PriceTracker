using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PriceTracker.Services;

namespace PriceTracker.Views;

public partial class MainWindow : Window
{
    private const double DragZone = 52;

    public MainWindow()
    {
        InitializeComponent();

        var s = App.Tracker!.Settings;
        Width = Math.Max(MinWidth, s.WinW);
        Height = Math.Max(MinHeight, s.WinH);
        if (s.WinX >= 0 && s.WinY >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(s.WinX, s.WinY);
        }

        CloseLight.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Close(); };
        MinLight.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) WindowState = WindowState.Minimized; };
        MaxLight.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) ToggleMaximize(); };
        foreach (var p in new[] { CloseLight, MinLight, MaxLight })
            p.PointerPressed += (_, e) => e.Handled = true;

        AddHandler(PointerPressedEvent, OnAnyPointerPressed, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: false);
        Opened += OnOpened;
        Closing += (_, _) => SaveBounds();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// The top strip of the window works like a macOS title bar: drag to move, double-click to zoom.
    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos.Y > DragZone || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual v && v.GetSelfAndVisualAncestors().Any(a => a is Button or TextBox or ComboBox or ListBoxItem or ToggleButton or Slider))
            return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        BeginMoveDrag(e);
    }

    private void SaveBounds()
    {
        if (Environment.GetEnvironmentVariable("PRICETRACKER_SCREENSHOT") != null) return;
        var s = App.Tracker!.Settings;
        if (WindowState == WindowState.Normal)
        {
            s.WinW = Width; s.WinH = Height;
            s.WinX = Position.X; s.WinY = Position.Y;
        }
        Storage.SaveSettings(s);
    }

    // ── Screenshot mode (used by scripts/screenshots.ps1 to render README images) ──
    private async void OnOpened(object? sender, EventArgs e)
    {
        if (Environment.GetEnvironmentVariable("PRICETRACKER_SELFTEST") is { Length: > 0 } testLog)
        {
            await SelfTest.RunAsync(testLog);
            Close();
            return;
        }
        var path = Environment.GetEnvironmentVariable("PRICETRACKER_SCREENSHOT");
        if (path == null) return;
        try { await CaptureAsync(path); }
        catch (Exception ex) { File.WriteAllText(path + ".error.txt", ex.ToString()); }
        Close();
    }

    private async Task CaptureAsync(string path)
    {
        var page = Environment.GetEnvironmentVariable("PRICETRACKER_PAGE") ?? "Games";
        App.Main!.Navigate(page);
        await Task.Delay(4500);   // cover art downloads
        Dispatcher.UIThread.RunJobs();

        Root.CornerRadius = new CornerRadius(12);
        Root.BorderThickness = new Thickness(1);
        if (this.TryFindResource("CardBorder", ActualThemeVariant, out var border)) Root.BorderBrush = border as Avalonia.Media.IBrush;
        await Task.Delay(300);
        var scale = RenderScaling;
        var size = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        Background = null;
        bmp.Render(Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bmp.Save(path);
    }
}
