using System.Text.Json;
using System.Text.Json.Serialization;

namespace PriceTracker.Services;

public sealed class GhSearchHit
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("img")] public string? Image { get; set; }
    [JsonPropertyName("price")] public string? PriceText { get; set; }
    [JsonPropertyName("offers")] public string? Offers { get; set; }
    [JsonPropertyName("variants")] public bool Variants { get; set; }
    [JsonIgnore] public decimal? Price => Http.ParsePrice(PriceText);
}

public sealed class GhProduct
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("img")] public string? Image { get; set; }
    [JsonPropertyName("prices")] public List<GhOffer> Offers { get; set; } = new();
    [JsonPropertyName("from")] public string? FromText { get; set; }
    [JsonPropertyName("used")] public GhUsed? Used { get; set; }
}

public sealed class GhOffer
{
    [JsonPropertyName("p")] public string? PriceText { get; set; }
    [JsonPropertyName("shop")] public string? Shop { get; set; }
    [JsonIgnore] public decimal? Price => Http.ParsePrice(PriceText);
}

public sealed class GhUsed
{
    [JsonPropertyName("p")] public string? PriceText { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonIgnore] public decimal? Price => Http.ParsePrice(PriceText);
}

/// Geizhals search and product pages, read through the embedded browser (Geizhals blocks plain HTTP clients).
public static class GeizhalsService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static string SearchUrl(string domain, string query) =>
        $"https://{domain}/?fs={Uri.EscapeDataString(query)}&hloc={(domain.EndsWith(".at") ? "at" : "de")}";

    private const string SearchScript = """
        (() => {
          const out = [];
          const seen = new Set();
          for (const art of document.querySelectorAll('article, .productlist__product, .listview__item')) {
            const links = [...art.querySelectorAll('a[href]')].filter(a => /-[av]\d+\.html/.test(a.href));
            if (!links.length) continue;
            const nameLink = art.querySelector('[class*="name-link"], [class*="__name"] a, h3 a') || links[0];
            const url = nameLink.href.split('#')[0].split('?')[0];
            if (seen.has(url)) continue;
            seen.add(url);
            const img = art.querySelector('img');
            const price = art.querySelector('.price, .gh_price, [class*="price"] .price');
            const offers = art.querySelector('[class*="offercount"]');
            out.push({
              name: (nameLink.getAttribute('title') || nameLink.textContent || '').trim().replace(/\s+/g, ' '),
              url,
              img: img ? (img.currentSrc || img.src) : null,
              price: price ? price.textContent.trim() : null,
              offers: offers ? offers.textContent.trim().replace(/\s+/g, ' ') : null,
              variants: /-v\d+\.html/.test(url)
            });
          }
          return JSON.stringify(out.slice(0, 36));
        })()
        """;

    private const string ProductScript = """
        (() => {
          const og = (p) => document.querySelector(`meta[property="${p}"]`)?.content || null;
          let title = og('og:title') || document.title || '';
          let name = title.split(/\s+ab\s+€/)[0].split(' | ')[0].trim();
          const from = (document.title.match(/ab\s+€\s*[\d.,]+/) || [null])[0];
          const prices = [];
          for (const o of document.querySelectorAll('.offer')) {
            const p = o.querySelector('.gh_price');
            if (!p) continue;
            const shop = o.querySelector('[data-merchant-name]')?.getAttribute('data-merchant-name')
                      || o.querySelector('.offer__merchant a, .merchant__logo-caption')?.textContent?.trim() || null;
            prices.push({ p: p.textContent.trim(), shop });
          }
          // Refurbished / used offers are listed in Geizhals' "Refurbished" box.
          const norm = (s) => (s || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
          const want = norm(name);
          let used = null;
          for (const li of document.querySelectorAll('.toplist__listitem')) {
            const link = li.querySelector('.toplist__text .toplist__link');
            if (!link) continue;
            const n = norm(link.childNodes[0]?.textContent || link.textContent);
            if (!n || !(n === want || want.startsWith(n) || n.startsWith(want))) continue;
            const p = li.querySelector('.gh_price');
            if (!p) continue;
            const label = li.querySelector('.toplist__link--secondary .notrans')?.textContent?.trim() || 'Refurbished';
            const cand = { p: p.textContent.trim(), label, url: link.href };
            const val = (s) => parseFloat(s.replace(/[^\d,]/g, '').replace(',', '.'));
            if (!used || val(cand.p) < val(used.p)) used = cand;
          }
          return JSON.stringify({ name, img: og('og:image'), prices, from, used });
        })()
        """;

    public static async Task<List<GhSearchHit>> SearchAsync(string domain, string query, CancellationToken ct = default)
    {
        var json = await BrowserFetcher.Instance.RunAsync(SearchUrl(domain, query), SearchScript, ct);
        if (json == null) return new();
        return JsonSerializer.Deserialize<List<GhSearchHit>>(json, Json) ?? new();
    }

    public static async Task<GhProduct?> GetProductAsync(string url, CancellationToken ct = default)
    {
        var json = await BrowserFetcher.Instance.RunAsync(url, ProductScript, ct);
        return json == null ? null : JsonSerializer.Deserialize<GhProduct>(json, Json);
    }
}
