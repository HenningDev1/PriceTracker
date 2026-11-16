using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using PriceTracker.Views;

namespace PriceTracker.Services;

/// Loads pages in an embedded Edge (WebView2) instance, exactly like a normal browser visit.
/// Some sites (Geizhals) only serve pages to real browsers. If a site asks for a human check,
/// the browser window is shown so the user can complete it themselves; the app never solves it.
public sealed class BrowserFetcher
{
    public static BrowserFetcher Instance { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private BrowserWindow? _window;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _web;

    private static readonly string[] ChallengeTitles =
    {
        "just a moment", "einen moment", "sichere verbindung", "attention required", "verifying", "überprüfung",
        "checking your browser", "security check",
    };

    public static bool IsAvailable
    {
        get
        {
            try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch { return false; }
        }
    }

    /// Navigates to <paramref name="url"/> and evaluates <paramref name="script"/>, which must return a string
    /// (usually JSON.stringify(...)). Calls are serialised; one page at a time.
    public async Task<string?> RunAsync(string url, string script, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => RunOnUiAsync(url, script, ct));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureAsync()
    {
        if (_web != null) return;
        _window = new BrowserWindow();
        var hwnd = _window.EnsureHandle();
        // The host window is normally hidden; keep pages running at full speed anyway.
        var options = new CoreWebView2EnvironmentOptions(
            "--disable-features=CalculateNativeWinOcclusion --disable-background-timer-throttling --disable-renderer-backgrounding");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Storage.CacheDir, "WebView2"), options);
        _controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        _controller.IsVisible = true;
        _web = _controller.CoreWebView2;
        _web.Settings.AreDevToolsEnabled = false;
        _web.Settings.IsStatusBarEnabled = false;
        _web.Settings.AreDefaultContextMenusEnabled = false;
        _web.Settings.IsGeneralAutofillEnabled = false;
        _web.Settings.IsPasswordAutosaveEnabled = false;
        _web.Settings.UserAgent = _web.Settings.UserAgent; // keep Edge's real UA
        // Never let pages open popups.
        _web.NewWindowRequested += (_, e) => e.Handled = true;
        _window.BoundsChanged += () => ResizeController();
        ResizeController();
    }

    private void ResizeController()
    {
        if (_controller == null || _window == null) return;
        _controller.Bounds = _window.WebViewBounds();
    }

    private async Task<string?> RunOnUiAsync(string url, string script, CancellationToken ct)
    {
        await EnsureAsync();
        var web = _web!;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
        web.NavigationCompleted += Done;
        try
        {
            web.Navigate(url);
            var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(40), ct));
            if (finished != tcs.Task) throw new TimeoutException($"{new Uri(url).Host} did not respond in time.");
        }
        finally
        {
            web.NavigationCompleted -= Done;
        }

        // Automatic browser checks usually finish on their own within a few seconds.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (IsChallenge(web.DocumentTitle) && DateTime.UtcNow < deadline)
            await Task.Delay(500, ct);

        if (IsChallenge(web.DocumentTitle))
        {
            // Hand over to the user: show the page and wait until they've passed the check (max 3 minutes).
            _window!.ShowForVerification(new Uri(url).Host);
            try
            {
                deadline = DateTime.UtcNow.AddMinutes(3);
                while (IsChallenge(web.DocumentTitle) && DateTime.UtcNow < deadline && _window.IsVisible)
                    await Task.Delay(700, ct);
            }
            finally
            {
                _window.HideAfterVerification();
            }
            if (IsChallenge(web.DocumentTitle))
                throw new InvalidOperationException($"{new Uri(url).Host} asked for a browser check that wasn't completed.");
            await Task.Delay(1200, ct);
        }

        var raw = await web.ExecuteScriptAsync(script);
        if (string.IsNullOrEmpty(raw) || raw == "null") return null;
        return JsonSerializer.Deserialize<string>(raw);
    }

    private static bool IsChallenge(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var t = title.ToLowerInvariant();
        return ChallengeTitles.Any(t.Contains);
    }
}
