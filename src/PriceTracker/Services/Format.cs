using System.Globalization;

namespace PriceTracker.Services;

public static class Format
{
    public static readonly CultureInfo Euro = CultureInfo.GetCultureInfo("de-DE");

    public static string Money(decimal? v) => v.HasValue ? v.Value.ToString("#,##0.00 €", Euro) : "—";

    public static string Ago(DateTime? t)
    {
        if (t == null) return "never";
        var d = DateTime.Now - t.Value;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 2) return "yesterday";
        return $"{(int)d.TotalDays} days ago";
    }
}
