using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using OtterGui.Services;
using Kaleidoscope.Services.Database;

namespace Kaleidoscope.Services.Universalis;

/// <summary>
/// Cache service for sale price lookups with TTL support.
/// Provides cache-first access to <see cref="KaleidoscopeDbService.GetMostRecentSalePrice"/>,
/// <see cref="KaleidoscopeDbService.GetMostRecentSalePriceForWorld"/>, and 
/// <see cref="KaleidoscopeDbService.GetLatestSalePrices"/> to avoid repeated DB queries.
/// </summary>
/// <remarks>
/// <para>
/// This cache is specifically designed for sale price lookups used in outlier filtering
/// during render loops. These lookups were previously hitting the DB directly on every frame,
/// causing significant performance overhead.
/// </para>
/// <para>
/// Cache entries have a configurable TTL (default 30 seconds) after which they become stale.
/// Stale entries are refreshed on next access. WebSocket sale events can update the cache
/// directly to maintain freshness without DB queries.
/// </para>
/// </remarks>
public sealed class SalePriceCacheService : IService, IDisposable
{
    private readonly IPluginLog _log;
    private readonly KaleidoscopeDbService _dbService;
    
    private readonly ConcurrentDictionary<(int ItemId, bool IsHq), SalePriceCacheEntry> _globalSaleCache = new();
    private readonly ConcurrentDictionary<(int ItemId, int WorldId, bool IsHq), SalePriceCacheEntry> _worldSaleCache = new();
    private readonly ConcurrentDictionary<int, BatchSalePriceCacheEntry> _batchSaleCache = new();
    private readonly ConcurrentDictionary<(int ItemId, bool IsHq), byte> _globalRefreshInFlight = new();
    private readonly ConcurrentDictionary<(int ItemId, int WorldId, bool IsHq), byte> _worldRefreshInFlight = new();
    
    private long _cacheHits;
    private long _cacheMisses;
    private long _dbFetches;
    
    private const int DefaultTtlSeconds = 30;
    private const int MaxCacheEntries = 20000;
    
    public int TtlSeconds { get; set; } = DefaultTtlSeconds;
    
    public SalePriceCacheService(IPluginLog log, KaleidoscopeDbService dbService)
    {
        _log = log;
        _dbService = dbService;
        LogService.Debug(LogCategory.Cache, "[SalePriceCache] Service initialized");
    }
    
    public long CacheHits => Interlocked.Read(ref _cacheHits);
    
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);
    
    public long DbFetches => Interlocked.Read(ref _dbFetches);
    
    public int GlobalCacheCount => _globalSaleCache.Count;
    
    public int WorldCacheCount => _worldSaleCache.Count;
    
    public int BatchCacheCount => _batchSaleCache.Count;
    
    /// <summary>Cache hit rate as a percentage (0-100).</summary>
    public double HitRate
    {
        get
        {
            var total = CacheHits + CacheMisses;
            return total > 0 ? (CacheHits * 100.0 / total) : 0;
        }
    }
    
    /// <summary>
    /// Gets the most recent sale price for an item.
    /// Serves cached (possibly stale) values and refreshes from DB off-thread; never blocks the caller.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="isHq">Whether to get HQ price.</param>
    /// <returns>The most recent sale price, or 0 if not found or not yet cached.</returns>
    public int GetMostRecentSalePrice(int itemId, bool isHq)
    {
        var key = (itemId, isHq);

        if (_globalSaleCache.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired(TtlSeconds))
            {
                Interlocked.Increment(ref _cacheHits);
                return entry.Price;
            }

            // Stale - serve the old value and refresh off-thread so draw paths never block on the DB.
            Interlocked.Increment(ref _cacheMisses);
            RefreshGlobalPriceAsync(key);
            return entry.Price;
        }

        Interlocked.Increment(ref _cacheMisses);
        RefreshGlobalPriceAsync(key);
        return 0;
    }

    private void RefreshGlobalPriceAsync((int ItemId, bool IsHq) key)
    {
        if (_dbService == null) return;
        if (!_globalRefreshInFlight.TryAdd(key, 0)) return;

        _ = Task.Run(() =>
        {
            try
            {
                Interlocked.Increment(ref _dbFetches);
                var price = _dbService.GetMostRecentSalePrice(key.ItemId, key.IsHq);
                _globalSaleCache[key] = new SalePriceCacheEntry(price);

                if (_globalSaleCache.Count > MaxCacheEntries)
                    EvictOldestEntries(_globalSaleCache, MaxCacheEntries / 10);
            }
            finally
            {
                _globalRefreshInFlight.TryRemove(key, out _);
            }
        });
    }
    
    /// <summary>
    /// Updates the global sale price cache directly (e.g., from WebSocket events).
    /// </summary>
    public void UpdateGlobalSalePrice(int itemId, bool isHq, int price)
    {
        if (price <= 0) return;
        var key = (itemId, isHq);
        _globalSaleCache[key] = new SalePriceCacheEntry(price);
    }
    
    /// <summary>
    /// Gets the most recent sale price for an item on a specific world.
    /// Serves cached (possibly stale) values and refreshes from DB off-thread; never blocks the caller.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="worldId">The world ID.</param>
    /// <param name="isHq">Whether to get HQ price.</param>
    /// <returns>The most recent sale price, or 0 if not found or not yet cached.</returns>
    public int GetMostRecentSalePriceForWorld(int itemId, int worldId, bool isHq)
    {
        var key = (itemId, worldId, isHq);

        if (_worldSaleCache.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired(TtlSeconds))
            {
                Interlocked.Increment(ref _cacheHits);
                return entry.Price;
            }

            // Stale - serve the old value and refresh off-thread so draw paths never block on the DB.
            Interlocked.Increment(ref _cacheMisses);
            RefreshWorldPriceAsync(key);
            return entry.Price;
        }

        Interlocked.Increment(ref _cacheMisses);
        RefreshWorldPriceAsync(key);
        return 0;
    }

    private void RefreshWorldPriceAsync((int ItemId, int WorldId, bool IsHq) key)
    {
        if (_dbService == null) return;
        if (!_worldRefreshInFlight.TryAdd(key, 0)) return;

        _ = Task.Run(() =>
        {
            try
            {
                Interlocked.Increment(ref _dbFetches);
                var price = _dbService.GetMostRecentSalePriceForWorld(key.ItemId, key.WorldId, key.IsHq);
                _worldSaleCache[key] = new SalePriceCacheEntry(price);

                if (_worldSaleCache.Count > MaxCacheEntries)
                    EvictOldestEntries(_worldSaleCache, MaxCacheEntries / 10);
            }
            finally
            {
                _worldRefreshInFlight.TryRemove(key, out _);
            }
        });
    }
    
    /// <summary>
    /// Updates the world-specific sale price cache directly (e.g., from WebSocket events).
    /// </summary>
    public void UpdateWorldSalePrice(int itemId, int worldId, bool isHq, int price)
    {
        if (price <= 0) return;
        var key = (itemId, worldId, isHq);
        _worldSaleCache[key] = new SalePriceCacheEntry(price);
    }
    
    /// <summary>
    /// Gets the latest sale prices for multiple items.
    /// Cache-first: returns cached values if within TTL, fetches missing/expired from DB.
    /// </summary>
    /// <param name="itemIds">Item IDs to get prices for.</param>
    /// <param name="includedWorldIds">Optional world filter.</param>
    /// <param name="excludedWorldIds">Optional world exclusion filter.</param>
    /// <param name="maxAge">Optional maximum age for sale records.</param>
    /// <returns>Dictionary of itemId -> (LastSaleNq, LastSaleHq).</returns>
    public Dictionary<int, (int LastSaleNq, int LastSaleHq)> GetLatestSalePrices(
        IEnumerable<int> itemIds,
        IEnumerable<int>? includedWorldIds = null,
        IEnumerable<int>? excludedWorldIds = null,
        TimeSpan? maxAge = null)
    {
        var result = new Dictionary<int, (int, int)>();
        var itemIdList = itemIds.ToList();
        var missingItems = new List<int>();
        
        foreach (var itemId in itemIdList)
        {
            if (_batchSaleCache.TryGetValue(itemId, out var entry) && !entry.IsExpired(TtlSeconds))
            {
                result[itemId] = (entry.LastSaleNq, entry.LastSaleHq);
                Interlocked.Increment(ref _cacheHits);
            }
            else
            {
                missingItems.Add(itemId);
                Interlocked.Increment(ref _cacheMisses);
            }
        }
        
        if (missingItems.Count > 0 && _dbService != null)
        {
            Interlocked.Increment(ref _dbFetches);
            var dbPrices = _dbService.GetLatestSalePrices(missingItems, includedWorldIds, excludedWorldIds, maxAge);
            
            foreach (var (itemId, prices) in dbPrices)
            {
                result[itemId] = prices;
                _batchSaleCache[itemId] = new BatchSalePriceCacheEntry(prices.LastSaleNq, prices.LastSaleHq);
            }
            
            foreach (var itemId in missingItems)
            {
                if (!dbPrices.ContainsKey(itemId))
                {
                    _batchSaleCache[itemId] = new BatchSalePriceCacheEntry(0, 0);
                }
            }
        }
        
        if (_batchSaleCache.Count > MaxCacheEntries)
        {
            EvictOldestBatchEntries(MaxCacheEntries / 10);
        }
        
        return result;
    }
    
    /// <summary>
    /// Updates the batch sale price cache directly (e.g., from WebSocket events).
    /// </summary>
    public void UpdateBatchSalePrice(int itemId, int? lastSaleNq, int? lastSaleHq)
    {
        if (_batchSaleCache.TryGetValue(itemId, out var existing))
        {
            var nq = lastSaleNq ?? existing.LastSaleNq;
            var hq = lastSaleHq ?? existing.LastSaleHq;
            _batchSaleCache[itemId] = new BatchSalePriceCacheEntry(nq, hq);
        }
        else
        {
            _batchSaleCache[itemId] = new BatchSalePriceCacheEntry(lastSaleNq ?? 0, lastSaleHq ?? 0);
        }
    }
    
    public void Clear()
    {
        _globalSaleCache.Clear();
        _worldSaleCache.Clear();
        _batchSaleCache.Clear();
        LogService.Debug(LogCategory.Cache, "[SalePriceCache] All caches cleared");
    }
    
    public void InvalidateItem(int itemId)
    {
        _globalSaleCache.TryRemove((itemId, false), out _);
        _globalSaleCache.TryRemove((itemId, true), out _);
        
        _batchSaleCache.TryRemove(itemId, out _);
        
        var worldKeysToRemove = _worldSaleCache.Keys.Where(k => k.ItemId == itemId).ToList();
        foreach (var key in worldKeysToRemove)
        {
            _worldSaleCache.TryRemove(key, out _);
        }
    }
    
    public SalePriceCacheStatistics GetStatistics()
    {
        return new SalePriceCacheStatistics
        {
            GlobalCacheEntries = _globalSaleCache.Count,
            WorldCacheEntries = _worldSaleCache.Count,
            BatchCacheEntries = _batchSaleCache.Count,
            CacheHits = CacheHits,
            CacheMisses = CacheMisses,
            DbFetches = DbFetches,
            HitRate = HitRate,
            TtlSeconds = TtlSeconds
        };
    }
    
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _dbFetches, 0);
    }
    
    private void EvictOldestEntries<TKey>(ConcurrentDictionary<TKey, SalePriceCacheEntry> cache, int count) where TKey : notnull
    {
        var toRemove = cache
            .OrderBy(kvp => kvp.Value.Timestamp)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in toRemove)
        {
            cache.TryRemove(key, out _);
        }
    }
    
    private void EvictOldestBatchEntries(int count)
    {
        var toRemove = _batchSaleCache
            .OrderBy(kvp => kvp.Value.Timestamp)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in toRemove)
        {
            _batchSaleCache.TryRemove(key, out _);
        }
    }
    
    public void Dispose()
    {
        _globalSaleCache.Clear();
        _worldSaleCache.Clear();
        _batchSaleCache.Clear();
    }
}

public sealed class SalePriceCacheEntry
{
    public int Price { get; }
    public DateTime Timestamp { get; }
    
    public SalePriceCacheEntry(int price)
    {
        Price = price;
        Timestamp = DateTime.UtcNow;
    }
    
    public bool IsExpired(int ttlSeconds) => (DateTime.UtcNow - Timestamp).TotalSeconds > ttlSeconds;
}

public sealed class BatchSalePriceCacheEntry
{
    public int LastSaleNq { get; }
    public int LastSaleHq { get; }
    public DateTime Timestamp { get; }
    
    public BatchSalePriceCacheEntry(int lastSaleNq, int lastSaleHq)
    {
        LastSaleNq = lastSaleNq;
        LastSaleHq = lastSaleHq;
        Timestamp = DateTime.UtcNow;
    }
    
    public bool IsExpired(int ttlSeconds) => (DateTime.UtcNow - Timestamp).TotalSeconds > ttlSeconds;
}

public record SalePriceCacheStatistics
{
    public int GlobalCacheEntries { get; init; }
    public int WorldCacheEntries { get; init; }
    public int BatchCacheEntries { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public long DbFetches { get; init; }
    public double HitRate { get; init; }
    public int TtlSeconds { get; init; }
}
