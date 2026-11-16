using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace PriceTracker.Services;

/// Downloads cover art / product photos once and keeps them in memory and on disk.
public static class ImageLoader
{
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();
    private static readonly SemaphoreSlim Parallel = new(6);
    private static string Dir => Path.Combine(Storage.CacheDir, "images");

    public static Task<Bitmap?> GetAsync(string? url, int decodeWidth = 460)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<Bitmap?>(null);
        return Cache.GetOrAdd(url + "|" + decodeWidth, _ => LoadAsync(url, decodeWidth));
    }

    private static async Task<Bitmap?> LoadAsync(string url, int width)
    {
        try
        {
            var file = Path.Combine(Dir, Hash(url));
            byte[] bytes;
            if (File.Exists(file) && DateTime.Now - File.GetLastWriteTime(file) < TimeSpan.FromDays(14))
            {
                bytes = await File.ReadAllBytesAsync(file);
            }
            else
            {
                await Parallel.WaitAsync();
                try { bytes = await Http.Client.GetByteArrayAsync(url); }
                finally { Parallel.Release(); }
                Directory.CreateDirectory(Dir);
                await File.WriteAllBytesAsync(file, bytes);
            }
            using var ms = new MemoryStream(bytes);
            return await Task.Run(() => Bitmap.DecodeToWidth(ms, width, BitmapInterpolationMode.HighQuality));
        }
        catch
        {
            return null;
        }
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..20];
}
