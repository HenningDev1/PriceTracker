using PriceTracker.Models;

namespace PriceTracker.Services;

public enum DealKind { AllTimeLow, BigDiscount, CheapKey, BelowUsual, UsedBargain }

public sealed record DealInfo(DealKind Kind, string Badge, string Reason, double Score);

/// When is something a deal? (Thresholds are adjustable in Settings.)
///
/// Games
///  • All-time low  – Steam price is discounted and at or below the lowest price ever seen
///                    (IsThereAnyDeal history if a key is set, otherwise the history this app recorded).
///  • Big discount  – Steam discount ≥ GameDealDiscount (default 60 %).
///  • Cheap key     – cheapest key ≥ KeyDealPercent (default 50 %) under Steam's regular price.
/// Items
///  • Below usual   – cheapest new price ≥ ItemDealPercent (default 15 %) under its usual price
///                    (median of the last 90 days, needs at least 3 days of history).
///  • All-time low  – new price lower than ever recorded (needs at least 7 days of history and ≥ 3 % under the usual price).
///  • Used bargain  – a used/refurbished offer ≥ 35 % under the cheapest new price.
public static class DealRules
{
    public const int UsedBargainPercent = 35;

    public static decimal? SteamAllTimeLow(GameEntry g)
    {
        var seen = g.History.Where(p => p.A.HasValue).Select(p => p.A!.Value).DefaultIfEmpty(decimal.MaxValue).Min();
        var low = g.SteamHistoricLow.HasValue ? Math.Min(g.SteamHistoricLow.Value, seen) : seen;
        return low == decimal.MaxValue ? null : low;
    }

    public static decimal? KeyAllTimeLow(GameEntry g) =>
        g.History.Where(p => p.B.HasValue).Select(p => p.B!.Value).DefaultIfEmpty().Min() is var m && m > 0 ? m : null;

    public static List<DealInfo> ForGame(GameEntry g, AppSettings s)
    {
        var deals = new List<DealInfo>();
        if (g.IsFree) return deals;

        if (g.SteamPrice is { } price && g.Discount > 0)
        {
            var atl = SteamAllTimeLow(g);
            if (atl.HasValue && price <= atl.Value + 0.005m)
                deals.Add(new DealInfo(DealKind.AllTimeLow, "All-time low", $"Steam -{g.Discount}% — lowest price ever", 100 + g.Discount));
            else if (g.Discount >= s.GameDealDiscount)
                deals.Add(new DealInfo(DealKind.BigDiscount, $"-{g.Discount}%", $"Steam sale: {g.Discount}% off", 50 + g.Discount));
        }

        var regular = g.SteamRegular ?? g.SteamPrice;
        if (g.KeyPrice is { } key && regular is > 0)
        {
            var off = (int)Math.Round((1 - key / regular.Value) * 100);
            if (off >= s.KeyDealPercent)
            {
                var keyAtl = KeyAllTimeLow(g);
                bool atl = keyAtl.HasValue && key <= keyAtl.Value + 0.005m && SpanDays(g.History) >= 7;
                deals.Add(new DealInfo(DealKind.CheapKey, atl ? "Key all-time low" : "Cheap key",
                    $"Key {off}% under Steam's regular price" + (g.KeyShop != null ? $" at {g.KeyShop}" : ""), 40 + off + (atl ? 20 : 0)));
            }
        }
        return deals;
    }

    public static decimal? UsualPrice(ItemEntry i)
    {
        var since = DateTime.Now.AddDays(-90);
        var values = i.History.Where(p => p.Time >= since && p.A.HasValue).Select(p => p.A!.Value).OrderBy(v => v).ToList();
        if (values.Count < 3 || SpanDays(i.History) < 3) return null;
        int mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    public static List<DealInfo> ForItem(ItemEntry i, AppSettings s)
    {
        var deals = new List<DealInfo>();
        if (i.NewPrice is { } now)
        {
            var usual = UsualPrice(i);
            if (usual is > 0)
            {
                var off = (double)(1 - now / usual.Value) * 100;
                var previous = i.History.Take(Math.Max(0, i.History.Count - 1)).Where(p => p.A.HasValue).Select(p => p.A!.Value).DefaultIfEmpty(decimal.MaxValue).Min();
                bool atl = now < previous && SpanDays(i.History) >= 7 && off >= 3;
                if (off >= s.ItemDealPercent)
                    deals.Add(new DealInfo(atl ? DealKind.AllTimeLow : DealKind.BelowUsual, atl ? "All-time low" : $"-{off:0}%",
                        $"{off:0}% under its usual price of {Format.Money(usual)}", 60 + off + (atl ? 30 : 0)));
                else if (atl)
                    deals.Add(new DealInfo(DealKind.AllTimeLow, "All-time low", $"Lowest price recorded ({off:0}% under usual)", 70 + off));
            }
        }
        if (i.UsedPrice is { } used && i.NewPrice is > 0)
        {
            var off = (double)(1 - used / i.NewPrice.Value) * 100;
            if (off >= UsedBargainPercent)
                deals.Add(new DealInfo(DealKind.UsedBargain, "Used bargain", $"{i.UsedLabel ?? "Used"}: {off:0}% under the cheapest new offer", 30 + off));
        }
        return deals;
    }

    private static double SpanDays(List<PricePoint> h) =>
        h.Count < 2 ? 0 : (h[^1].Time - h[0].Time).TotalDays;
}
