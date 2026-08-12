using Microsoft.Extensions.DependencyInjection;

namespace Decanterr.Api.Services;

/// <summary>
/// Caches the set of ASINs actually present in Audiobookshelf's libraries, refreshed periodically
/// in the background. This lets us report a book as "in Audiobookshelf" even if it was added there
/// independently of this app (e.g. before being unlocked/liberated here).
/// </summary>
public static class AudiobookshelfLibraryCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly object Lock = new();
    private static HashSet<string> _cachedAsins = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastRefresh = DateTime.MinValue;
    private static bool _refreshing;
    private static IServiceProvider? _services;

    public static void Configure(IServiceProvider services) => _services = services;

    /// <summary>Whether the ASIN exists in Audiobookshelf, per the most recent background refresh.</summary>
    public static bool Contains(string asin)
    {
        TriggerRefreshIfStale();
        lock (Lock)
            return _cachedAsins.Contains(asin);
    }

    private static void TriggerRefreshIfStale()
    {
        if (_services is null)
            return;

        lock (Lock)
        {
            if (_refreshing || DateTime.UtcNow - _lastRefresh < Ttl)
                return;
            _refreshing = true;
        }

        _ = RefreshAsync();
    }

    private static async Task RefreshAsync()
    {
        try
        {
            using var scope = _services!.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<AudiobookshelfClient>();
            if (!client.IsEnabled)
                return;

            var libraries = await client.GetLibrariesAsync();
            var asins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var library in libraries)
                foreach (var asin in await client.GetLibraryItemAsinsAsync(library.Id))
                    asins.Add(asin);

            lock (Lock)
            {
                _cachedAsins = asins;
                _lastRefresh = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Logger.Warning(ex, "Failed to refresh Audiobookshelf library cache");
        }
        finally
        {
            lock (Lock)
                _refreshing = false;
        }
    }
}
