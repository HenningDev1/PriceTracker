using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace PriceTracker.Services;

public static partial class Http
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36 PriceTracker/1.0";

    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE,de;q=0.9,en;q=0.8");
        return c;
    }

    /// Parses "€ 1.234,56", "1234.56", "59,99€" and similar into a decimal.
    public static decimal? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = NumberRx().Match(text);
        if (!m.Success) return null;
        var s = m.Value;
        int lastComma = s.LastIndexOf(','), lastDot = s.LastIndexOf('.');
        if (lastComma > lastDot)
            s = s.Replace(".", "").Replace(',', '.');               // 1.234,56
        else if (lastDot > lastComma && lastComma >= 0)
            s = s.Replace(",", "");                                 // 1,234.56
        else if (lastDot >= 0 && s.Length - lastDot - 1 == 3 && s.Count(c => c == '.') >= 1 && lastComma < 0 && !s.Contains(".-"))
        {
            // "1.234" is a thousands separator in German formatting; "12.99" stays a decimal.
            s = s.Replace(".", "");
        }
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex NumberRx();
}
