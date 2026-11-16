using Avalonia;
using Avalonia.Controls;

namespace PriceTracker.Views;

/// Native parent for the embedded WebView2. Stays hidden; it only appears when a site asks
/// the user to confirm they're human, so the user can do that themselves.
public partial class BrowserWindow : Window
{
    public event Action? BoundsChanged;
    private bool _allowClose;

    public BrowserWindow()
    {
        InitializeComponent();
        Host.SizeChanged += (_, _) => BoundsChanged?.Invoke();
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;   // closing just hides it; the fetcher notices and gives up the wait
            Hide();
        };
    }

    public IntPtr EnsureHandle()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero) return handle;
        // Fallback: realise the native window off-screen once.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(-32000, -32000);
        ShowActivated = false;
        Show();
        handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        Hide();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return handle;
    }

    public System.Drawing.Rectangle WebViewBounds()
    {
        var scale = RenderScaling;
        var origin = Host.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 64);
        var w = Host.Bounds.Width > 0 ? Host.Bounds.Width : Width;
        var h = Host.Bounds.Height > 0 ? Host.Bounds.Height : Height - 64;
        return new System.Drawing.Rectangle((int)(origin.X * scale), (int)(origin.Y * scale), (int)(w * scale), (int)(h * scale));
    }

    public void ShowForVerification(string host)
    {
        HeadLine.Text = $"{host} wants to confirm you're human";
        if (Position.X < -10000) Position = new PixelPoint(200, 120);
        ShowActivated = true;
        Show();
        Activate();
        BoundsChanged?.Invoke();
    }

    public void HideAfterVerification() => Hide();

    public void CloseForGood()
    {
        _allowClose = true;
        Close();
    }
}
