using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Fylax.Services;

public sealed class BlocklistLoader
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly string _cacheFolder;

    public BlocklistLoader()
    {
        _cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fylax", "cache");
        Directory.CreateDirectory(_cacheFolder);
    }

    public Task<IReadOnlyList<string>> LoadAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        return CombineAsync(urls, url => FetchAndCacheAsync(url, cancellationToken));
    }

    public Task<IReadOnlyList<string>> LoadCachedFirstAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        return CombineAsync(urls, url =>
        {
            var cachePath = CachePath(url);
            return File.Exists(cachePath) ? ReadCacheAsync(cachePath, cancellationToken) : FetchAndCacheAsync(url, cancellationToken);
        });
    }

    private static async Task<IReadOnlyList<string>> CombineAsync(IEnumerable<string> urls, Func<string, Task<IReadOnlyList<string>>> loader)
    {
        var valid = urls
            .Select(url => url.Trim())
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var results = await Task.WhenAll(valid.Select(loader));
        var lines = new List<string>();
        foreach (var result in results)
        {
            lines.AddRange(result);
        }
        return lines;
    }

    private async Task<IReadOnlyList<string>> FetchAndCacheAsync(string url, CancellationToken cancellationToken)
    {
        var cachePath = CachePath(url);
        try
        {
            var text = await _httpClient.GetStringAsync(url, cancellationToken);
            var fresh = Split(text);
            if (File.Exists(cachePath))
            {
                var cached = await ReadCacheAsync(cachePath, cancellationToken);
                if (cached.Count >= 100 && fresh.Length < cached.Count / 3)
                {
                    return cached;
                }
            }
            await File.WriteAllTextAsync(cachePath, text, cancellationToken);
            return fresh;
        }
        catch
        {
            if (File.Exists(cachePath))
            {
                return await ReadCacheAsync(cachePath, cancellationToken);
            }
            return Array.Empty<string>();
        }
    }

    private static async Task<IReadOnlyList<string>> ReadCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        var cached = await File.ReadAllTextAsync(cachePath, cancellationToken);
        return Split(cached);
    }

    private string CachePath(string url)
    {
        return Path.Combine(_cacheFolder, HashUrl(url) + ".txt");
    }

    private static string[] Split(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string HashUrl(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes);
    }
}
