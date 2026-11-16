using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using PriceTracker.Models;

namespace PriceTracker.Services;

public sealed record AccentOption(string Name, Color Color)
{
    public IBrush Brush => new SolidColorBrush(Color);
}

/// Applies the user's appearance choices (light/dark, accent colour, font, text size) at runtime.
public static class ThemeService
{
    public static readonly AccentOption[] Accents =
    {
        new("Green", Color.Parse("#10B981")),
        new("Blue", Color.Parse("#0A84FF")),
        new("Indigo", Color.Parse("#5E5CE6")),
        new("Purple", Color.Parse("#BF5AF2")),
        new("Pink", Color.Parse("#FF375F")),
        new("Red", Color.Parse("#FF453A")),
        new("Orange", Color.Parse("#FF9F0A")),
        new("Yellow", Color.Parse("#E6B800")),
        new("Teal", Color.Parse("#30B0C7")),
        new("Graphite", Color.Parse("#8E8E93")),
    };

    public static readonly string[] Modes = { "System", "Light", "Dark" };

    public const string InterName = "Inter";

    public static List<string> AvailableFonts()
    {
        var list = new List<string> { InterName };
        var preferred = new[] { "Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", "Arial", "Calibri", "Verdana", "Bahnschrift", "Consolas" };
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        list.AddRange(preferred.Where(installed.Contains));
        list.AddRange(installed.Where(f => !list.Contains(f, StringComparer.OrdinalIgnoreCase)).OrderBy(f => f));
        return list;
    }

    public static void Apply(AppSettings s)
    {
        var app = Application.Current;
        if (app == null) return;

        app.RequestedThemeVariant = s.ThemeMode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        var accent = Accents.FirstOrDefault(a => a.Name == s.Accent) ?? Accents[0];
        var c = accent.Color;
        var r = app.Resources;
        r["AccentColor"] = c;
        r["AccentBrush"] = new SolidColorBrush(c);
        r["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
        r["AccentForegroundBrush"] = new SolidColorBrush(Luma(c) > 0.62 ? Color.Parse("#1D1D1F") : Colors.White);
        // Fluent controls (toggles, sliders, focus rings) follow the accent too.
        r["SystemAccentColor"] = c;
        r["SystemAccentColorLight1"] = Shade(c, 0.15);
        r["SystemAccentColorLight2"] = Shade(c, 0.30);
        r["SystemAccentColorLight3"] = Shade(c, 0.45);
        r["SystemAccentColorDark1"] = Shade(c, -0.12);
        r["SystemAccentColorDark2"] = Shade(c, -0.24);
        r["SystemAccentColorDark3"] = Shade(c, -0.36);

        r["AppFont"] = s.FontFamily == InterName || string.IsNullOrWhiteSpace(s.FontFamily)
            ? new FontFamily("fonts:Inter#Inter")
            : new FontFamily(s.FontFamily);

        var k = Math.Clamp(s.FontScale, 0.8, 1.4);
        r["FontXS"] = 11 * k;
        r["FontS"] = 12 * k;
        r["FontM"] = 13.5 * k;
        r["FontL"] = 15.5 * k;
        r["FontXL"] = 20 * k;
        r["FontXXL"] = 28 * k;
    }

    private static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static Color Shade(Color c, double amount)
    {
        byte Mix(byte v) => amount >= 0
            ? (byte)(v + (255 - v) * amount)
            : (byte)(v * (1 + amount));
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }
}
