using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services.Universalis;

/// <summary>
/// Centralized cache for market/price data with TTL support and staleness indicators.
/// Provides cache-first access to item prices with configurable freshness thresholds.
/// </summary>
public sealed class MarketDataCacheService : IService, IDisposable
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    
    private readonly ConcurrentDictionary<(int ItemId, int WorldId), MarketPriceCacheEntry> _priceCache = new();
    
    // Secondary indexes for O(1) lookups instead of O(N) full-cache scans
    private readonly ConcurrentDictionary<int, HashSet<int>> _itemToWorlds = new(); // itemId → set of worldIds
    private readonly ConcurrentDictionary<int, HashSet<int>> _worldToItems = new(); // worldId → set of itemIds
    private readonly object _indexLock = new(); // Protects compound index updates
    
    private readonly ConcurrentDictionary<(int ItemId, int WorldId), RecentSalesCacheEntry> _recentSalesCache = new();
    
    private readonly ConcurrentDictionary<(int ItemId, bool IsHq), int> _lastSalePriceCache = new();
    
    private long _cacheHits;
    private long _cacheMisses;
    private long _staleHits;
    private long _evictions;
    private long _version;
    private DateTime? _lastEvictionTime;
    
    private const int DefaultTtlMinutes = 15;
    private const int DefaultStalenessThresholdMinutes = 60;
    private const int MaxCacheEntries = 50000; // Prevent unbounded growth
    
    public MarketDataCacheService(IPluginLog log, ConfigurationService configService)
    {
        _log = log;
        _configService = configService;
        LogService.Debug(LogCategory.Cache, "[MarketDataCache] Service initialized");
    }
    
    #region Secondary Index Helpers
    
    /// <summary>
    /// Adds an (itemId, worldId) pair to both secondary indexes.
    /// </summary>
    private void AddToIndexes(int itemId, int worldId)
    {
        lock (_indexLock)
        {
            if (!_itemToWorlds.TryGetValue(itemId, out var worlds))
            {
                worlds = new HashSet<int>();
                _itemToWorlds[itemId] = worlds;
            }
            worlds.Add(worldId);
            
            if (!_worldToItems.TryGetValue(worldId, out var items))
            {
                items = new HashSet<int>();
                _worldToItems[worldId] = items;
            }
            items.Add(itemId);
        }
    }
    
    /// <summary>
    /// Removes an (itemId, worldId) pair from both secondary indexes.
    /// </summary>
    private void RemoveFromIndexes(int itemId, int worldId)
    {
        lock (_indexLock)
        {
            if (_itemToWorlds.TryGetValue(itemId, out var worlds))
            {
                worlds.Remove(worldId);
                if (worlds.Count == 0)
                    _itemToWorlds.TryRemove(itemId, out _);
            }
            
            if (_worldToItems.TryGetValue(worldId, out var items))
            {
                items.Remove(itemId);
                if (items.Count == 0)
                    _worldToItems.TryRemove(worldId, out _);
            }
        }
    }
    
    #endregion
    
    #region Public Properties - Statistics
    
    /// <summary>
    /// Monotonically increasing version counter. Incremented on every cache mutation.
    /// Consumers can compare against a stored version to detect changes without polling.
    /// </summary>
    public long Version => Volatile.Read(ref _version);
    
    /// <summary>Number of cache hits (fresh data returned).</summary>
    public long CacheHits => Interlocked.Read(ref _cacheHits);
    
    /// <summary>Number of cache misses (no data in cache).</summary>
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);
    
    /// <summary>Number of stale hits (data returned but marked as stale).</summary>
    public long StaleHits => Interlocked.Read(ref _staleHits);
    
    /// <summary>Number of cache evictions due to size limits.</summary>
    public long Evictions => Interlocked.Read(ref _evictions);
    
    /// <summary>Total entries in the price cache.</summary>
    public int PriceCacheCount => _priceCache.Count;
    
    /// <summary>Total entries in the recent sales cache.</summary>
    public int RecentSalesCacheCount => _recentSalesCache.Count;
    
    /// <summary>Cache hit rate as a percentage (0-100).</summary>
    public double HitRate
    {
        get
        {
            var total = CacheHits + CacheMisses;
            return total > 0 ? (CacheHits * 100.0 / total) : 0;
        }
    }
    
    /// <summary>Last time cache eviction was performed.</summary>
    public DateTime? LastEvictionTime => _lastEvictionTime;
    
    #endregion
    
    #region Price Cache Operations
    
    /// <summary>
    /// Gets a cached price entry if available.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="worldId">The world ID.</param>
    /// <param name="entry">The cache entry if found.</param>
    /// <returns>True if found (may be stale), false if not in cache.</returns>
    public bool TryGetPrice(int itemId, int worldId, out MarketPriceCacheEntry? entry)
    {
        var key = (itemId, worldId);
        if (_priceCache.TryGetValue(key, out entry))
        {
            if (entry.IsFresh)
            {
                Interlocked.Increment(ref _cacheHits);
            }
            else
            {
                Interlocked.Increment(ref _staleHits);
            }
            return true;
        }
        
        entry = null;
        Interlocked.Increment(ref _cacheMisses);
        return false;
    }
    
    /// <summary>
    /// Gets a cached price, or null if not in cache.
    /// </summary>
    public (int MinNq, int MinHq)? GetPrice(int itemId, int worldId)
    {
        if (TryGetPrice(itemId, worldId, out var entry) && entry != null)
        {
            return (entry.MinPriceNq, entry.MinPriceHq);
        }
        return null;
    }
    
    /// <summary>
    /// Gets a cached price with freshness information.
    /// </summary>
    public MarketPriceCacheEntry? GetPriceWithMetadata(int itemId, int worldId)
    {
        TryGetPrice(itemId, worldId, out var entry);
        return entry;
    }
    
    /// <summary>
    /// Sets or updates a price in the cache.
    /// </summary>
    public void SetPrice(int itemId, int worldId, int minPriceNq, int minPriceHq, 
        int lastSaleNq = 0, int lastSaleHq = 0, PriceSource source = PriceSource.Unknown)
    {
        var key = (itemId, worldId);
        var now = DateTime.UtcNow;
        
        var entry = new MarketPriceCacheEntry
        {
            ItemId = itemId,
            WorldId = worldId,
            MinPriceNq = minPriceNq,
            MinPriceHq = minPriceHq,
            LastSaleNq = lastSaleNq,
            LastSaleHq = lastSaleHq,
            LastUpdated = now,
            Source = source,
            TtlMinutes = DefaultTtlMinutes,
            StalenessThresholdMinutes = DefaultStalenessThresholdMinutes
        };
        
        _priceCache[key] = entry;
        AddToIndexes(itemId, worldId);
        
        if (lastSaleNq > 0)
        {
            _lastSalePriceCache[(itemId, false)] = lastSaleNq;
        }
        if (lastSaleHq > 0)
        {
            _lastSalePriceCache[(itemId, true)] = lastSaleHq;
        }
        
        if (_priceCache.Count > MaxCacheEntries)
        {
            EvictOldestEntries(MaxCacheEntries / 10); // Evict 10%
        }
        Interlocked.Increment(ref _version);
    }
    
    /// <summary>
    /// Updates only the min listing prices (from WebSocket listing events).
    /// Uses atomic replacement to avoid torn reads from concurrent accessors.
    /// </summary>
    public void UpdateMinPrices(int itemId, int worldId, int? minPriceNq, int? minPriceHq)
    {
        var key = (itemId, worldId);
        
        _priceCache.AddOrUpdate(key,
            // Add factory: no existing entry, create a new one
            _ => new MarketPriceCacheEntry
            {
                ItemId = itemId,
                WorldId = worldId,
                MinPriceNq = minPriceNq ?? 0,
                MinPriceHq = minPriceHq ?? 0,
                LastUpdated = DateTime.UtcNow,
                Source = PriceSource.WebSocket,
                TtlMinutes = DefaultTtlMinutes,
                StalenessThresholdMinutes = DefaultStalenessThresholdMinutes
            },
            // Update factory: replace with new entry using merged values
            (_, existing) =>
            {
                var newNq = minPriceNq.HasValue && minPriceNq.Value > 0
                    ? (existing.MinPriceNq > 0 ? Math.Min(existing.MinPriceNq, minPriceNq.Value) : minPriceNq.Value)
                    : existing.MinPriceNq;
                var newHq = minPriceHq.HasValue && minPriceHq.Value > 0
                    ? (existing.MinPriceHq > 0 ? Math.Min(existing.MinPriceHq, minPriceHq.Value) : minPriceHq.Value)
                    : existing.MinPriceHq;
                
                return new MarketPriceCacheEntry
                {
                    ItemId = itemId,
                    WorldId = worldId,
                    MinPriceNq = newNq,
                    MinPriceHq = newHq,
                    LastSaleNq = existing.LastSaleNq,
                    LastSaleHq = existing.LastSaleHq,
                    LastUpdated = DateTime.UtcNow,
                    Source = PriceSource.WebSocket,
                    TtlMinutes = existing.TtlMinutes,
                    StalenessThresholdMinutes = existing.StalenessThresholdMinutes
                };
            });
        Interlocked.Increment(ref _version);
    }
    
    /// <summary>
    /// Updates only the last sale prices (from WebSocket sale events).
    /// Uses atomic replacement to avoid torn reads from concurrent accessors.
    /// </summary>
    public void UpdateSalePrices(int itemId, int worldId, int? lastSaleNq, int? lastSaleHq)
    {
        var key = (itemId, worldId);
        
        if (lastSaleNq.HasValue && lastSaleNq.Value > 0)
            _lastSalePriceCache[(itemId, false)] = lastSaleNq.Value;
        if (lastSaleHq.HasValue && lastSaleHq.Value > 0)
            _lastSalePriceCache[(itemId, true)] = lastSaleHq.Value;
        
        _priceCache.AddOrUpdate(key,
            // Add factory: no existing entry, create a new one
            _ => new MarketPriceCacheEntry
            {
                ItemId = itemId,
                WorldId = worldId,
                LastSaleNq = lastSaleNq ?? 0,
                LastSaleHq = lastSaleHq ?? 0,
                LastUpdated = DateTime.UtcNow,
                Source = PriceSource.WebSocket,
                TtlMinutes = DefaultTtlMinutes,
                StalenessThresholdMinutes = DefaultStalenessThresholdMinutes
            },
            // Update factory: replace with new entry using merged values
            (_, existing) => new MarketPriceCacheEntry
            {
                ItemId = itemId,
                WorldId = worldId,
                MinPriceNq = existing.MinPriceNq,
                MinPriceHq = existing.MinPriceHq,
                LastSaleNq = lastSaleNq.HasValue && lastSaleNq.Value > 0 ? lastSaleNq.Value : existing.LastSaleNq,
                LastSaleHq = lastSaleHq.HasValue && lastSaleHq.Value > 0 ? lastSaleHq.Value : existing.LastSaleHq,
                LastUpdated = DateTime.UtcNow,
                Source = PriceSource.WebSocket,
                TtlMinutes = existing.TtlMinutes,
                StalenessThresholdMinutes = existing.StalenessThresholdMinutes
            });
        Interlocked.Increment(ref _version);
    }
    
    /// <summary>
    /// Gets all cached prices for a specific item across all worlds.
    /// Uses secondary index for O(1) lookup instead of scanning the entire cache.
    /// </summary>
    public IReadOnlyDictionary<int, MarketPriceCacheEntry> GetPricesForItem(int itemId)
    {
        var result = new Dictionary<int, MarketPriceCacheEntry>();
        HashSet<int>? worlds;
        lock (_indexLock)
        {
            if (!_itemToWorlds.TryGetValue(itemId, out worlds))
                return result;
            worlds = new HashSet<int>(worlds); // snapshot under lock
        }
        foreach (var worldId in worlds)
        {
            if (_priceCache.TryGetValue((itemId, worldId), out var entry))
            {
                result[worldId] = entry;
            }
        }
        return result;
    }
    
    /// <summary>
    /// Gets all cached prices for a specific world.
    /// Uses secondary index for O(1) lookup instead of scanning the entire cache.
    /// </summary>
    public IReadOnlyDictionary<int, MarketPriceCacheEntry> GetPricesForWorld(int worldId)
    {
        var result = new Dictionary<int, MarketPriceCacheEntry>();
        HashSet<int>? items;
        lock (_indexLock)
        {
            if (!_worldToItems.TryGetValue(worldId, out items))
                return result;
            items = new HashSet<int>(items); // snapshot under lock
        }
        foreach (var itemId in items)
        {
            if (_priceCache.TryGetValue((itemId, worldId), out var entry))
            {
                result[itemId] = entry;
            }
        }
        return result;
    }
    
    /// <summary>
    /// Batch retrieval of prices for multiple items.
    /// </summary>
    public Dictionary<int, MarketPriceCacheEntry?> GetPricesBatch(IEnumerable<int> itemIds, int worldId)
    {
        var result = new Dictionary<int, MarketPriceCacheEntry?>();
        foreach (var itemId in itemIds)
        {
            TryGetPrice(itemId, worldId, out var entry);
            result[itemId] = entry;
        }
        return result;
    }
    
    /// <summary>
    /// Gets all stale entries that should be refreshed.
    /// </summary>
    public IReadOnlyList<(int ItemId, int WorldId)> GetStaleEntries(int maxCount = 100)
    {
        return _priceCache
            .Where(kvp => kvp.Value.IsStale)
            .OrderBy(kvp => kvp.Value.LastUpdated)
            .Take(maxCount)
            .Select(kvp => kvp.Key)
            .ToList();
    }
    
    /// <summary>
    /// Gets all expired entries that should be evicted.
    /// </summary>
    public IReadOnlyList<(int ItemId, int WorldId)> GetExpiredEntries()
    {
        return _priceCache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();
    }
    
    /// <summary>
    /// Removes a price from the cache.
    /// </summary>
    public bool RemovePrice(int itemId, int worldId)
    {
        var removed = _priceCache.TryRemove((itemId, worldId), out _);
        if (removed)
        {
            RemoveFromIndexes(itemId, worldId);
        }
        return removed;
    }
    
    /// <summary>
    /// Clears all price cache entries.
    /// </summary>
    public void ClearPriceCache()
    {
        _priceCache.Clear();
        lock (_indexLock)
        {
            _itemToWorlds.Clear();
            _worldToItems.Clear();
        }
        Interlocked.Increment(ref _version);
        LogService.Debug(LogCategory.Cache, "[MarketDataCache] Price cache cleared");
    }
    
    private void EvictOldestEntries(int count)
    {
        var toEvict = _priceCache
            .OrderBy(kvp => kvp.Value.LastUpdated)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in toEvict)
        {
            if (_priceCache.TryRemove(key, out _))
            {
                RemoveFromIndexes(key.ItemId, key.WorldId);
                Interlocked.Increment(ref _evictions);
            }
        }
        
        _lastEvictionTime = DateTime.UtcNow;
        LogService.Debug(LogCategory.Cache, $"[MarketDataCache] Evicted {count} oldest entries");
    }
    
    #endregion
    
    #region Recent Sales Cache Operations
    
    /// <summary>
    /// Gets recent sales data for an item/world combination.
    /// </summary>
    public RecentSalesCacheEntry? GetRecentSales(int itemId, int worldId)
    {
        _recentSalesCache.TryGetValue((itemId, worldId), out var entry);
        return entry;
    }
    
    /// <summary>
    /// Sets recent sales data for an item/world combination.
    /// </summary>
    public void SetRecentSales(int itemId, int worldId, RecentSalesCacheEntry entry)
    {
        _recentSalesCache[(itemId, worldId)] = entry;
        Interlocked.Increment(ref _version);
    }
    
    /// <summary>
    /// Adds a sale price to the recent sales cache.
    /// </summary>
    public void AddRecentSale(int itemId, int worldId, int price, bool isHq)
    {
        var key = (itemId, worldId);
        var entry = _recentSalesCache.GetOrAdd(key, _ => new RecentSalesCacheEntry 
        { 
            ItemId = itemId, 
            WorldId = worldId 
        });
        
        entry.AddPrice(price, isHq);
        
        // Update last sale cache for spike detection
        _lastSalePriceCache[(itemId, isHq)] = price;
    }
    
    /// <summary>
    /// Gets the last known sale price for spike detection.
    /// </summary>
    public int GetLastSalePrice(int itemId, bool isHq)
    {
        _lastSalePriceCache.TryGetValue((itemId, isHq), out var price);
        return price;
    }
    
    /// <summary>
    /// Bulk loads recent sales data from database.
    /// </summary>
    public void LoadRecentSalesFromDb(IReadOnlyDictionary<(int ItemId, int WorldId), (List<int> NqPrices, List<int> HqPrices)> data)
    {
        foreach (var (key, prices) in data)
        {
            var entry = new RecentSalesCacheEntry
            {
                ItemId = key.ItemId,
                WorldId = key.WorldId
            };
            entry.SetPrices(prices.NqPrices, isHq: false);
            entry.SetPrices(prices.HqPrices, isHq: true);
            _recentSalesCache[key] = entry;
        }
        
        LogService.Debug(LogCategory.Cache, $"[MarketDataCache] Loaded {data.Count} recent sales entries from database");
    }
    
    /// <summary>
    /// Clears the recent sales cache.
    /// </summary>
    public void ClearRecentSalesCache()
    {
        _recentSalesCache.Clear();
        _lastSalePriceCache.Clear();
        Interlocked.Increment(ref _version);
        LogService.Debug(LogCategory.Cache, "[MarketDataCache] Recent sales cache cleared");
    }
    
    #endregion
    
    #region Statistics and Maintenance
    
    /// <summary>
    /// Resets all statistics counters.
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _staleHits, 0);
        Interlocked.Exchange(ref _evictions, 0);
        _lastEvictionTime = null;
    }
    
    /// <summary>
    /// Performs cache maintenance - removes expired entries.
    /// </summary>
    public int PerformMaintenance()
    {
        var expired = GetExpiredEntries();
        var count = 0;
        
        foreach (var key in expired)
        {
            if (_priceCache.TryRemove(key, out _))
            {
                RemoveFromIndexes(key.ItemId, key.WorldId);
                count++;
                Interlocked.Increment(ref _evictions);
            }
        }
        
        if (count > 0)
        {
            _lastEvictionTime = DateTime.UtcNow;
            LogService.Debug(LogCategory.Cache, $"[MarketDataCache] Maintenance removed {count} expired entries");
        }
        
        return count;
    }
    
    /// <summary>
    /// Gets a summary of cache state for debugging/display.
    /// </summary>
    public MarketCacheStatistics GetStatistics()
    {
        var now = DateTime.UtcNow;
        var freshCount = _priceCache.Count(kvp => kvp.Value.IsFresh);
        var staleCount = _priceCache.Count(kvp => kvp.Value.IsStale && !kvp.Value.IsExpired);
        var expiredCount = _priceCache.Count(kvp => kvp.Value.IsExpired);
        
        return new MarketCacheStatistics
        {
            TotalPriceEntries = _priceCache.Count,
            FreshEntries = freshCount,
            StaleEntries = staleCount,
            ExpiredEntries = expiredCount,
            RecentSalesEntries = _recentSalesCache.Count,
            LastSalePriceEntries = _lastSalePriceCache.Count,
            CacheHits = CacheHits,
            CacheMisses = CacheMisses,
            StaleHits = StaleHits,
            Evictions = Evictions,
            HitRate = HitRate,
            LastEvictionTime = _lastEvictionTime
        };
    }
    
    #endregion
    
    public void Dispose()
    {
        _priceCache.Clear();
        _recentSalesCache.Clear();
        _lastSalePriceCache.Clear();
        lock (_indexLock)
        {
            _itemToWorlds.Clear();
            _worldToItems.Clear();
        }
        LogService.Debug(LogCategory.Cache, "[MarketDataCache] Disposed");
    }
}

/// <summary>
/// Represents a cached market price entry with TTL and staleness tracking.
/// </summary>
public class MarketPriceCacheEntry
{
    public int ItemId { get; init; }
    public int WorldId { get; init; }
    public int MinPriceNq { get; init; }
    public int MinPriceHq { get; init; }
    public int LastSaleNq { get; init; }
    public int LastSaleHq { get; init; }
    public DateTime LastUpdated { get; init; }
    public PriceSource Source { get; init; }
    
    /// <summary>Time-to-live in minutes before data is considered stale.</summary>
    public int TtlMinutes { get; init; } = 15;
    
    /// <summary>Threshold in minutes after which data is considered expired.</summary>
    public int StalenessThresholdMinutes { get; init; } = 60;
    
    /// <summary>Age of the cache entry.</summary>
    public TimeSpan Age => DateTime.UtcNow - LastUpdated;
    
    /// <summary>Whether the data is still fresh (within TTL).</summary>
    public bool IsFresh => Age.TotalMinutes < TtlMinutes;
    
    /// <summary>Whether the data is stale but not expired.</summary>
    public bool IsStale => Age.TotalMinutes >= TtlMinutes && Age.TotalMinutes < StalenessThresholdMinutes;
    
    /// <summary>Whether the data is expired and should be evicted.</summary>
    public bool IsExpired => Age.TotalMinutes >= StalenessThresholdMinutes;
    
    /// <summary>Freshness indicator (0-1, where 1 is fresh and 0 is expired).</summary>
    public double Freshness
    {
        get
        {
            if (IsFresh) return 1.0;
            if (IsExpired) return 0.0;
            var staleRange = StalenessThresholdMinutes - TtlMinutes;
            var staleAge = Age.TotalMinutes - TtlMinutes;
            return 1.0 - (staleAge / staleRange);
        }
    }
}

/// <summary>
/// Source of the price data.
/// </summary>
public enum PriceSource
{
    Unknown,
    Database,
    ApiCall,
    WebSocket
}

/// <summary>
/// Cache entry for recent sales used in outlier detection.
/// </summary>
public class RecentSalesCacheEntry
{
    public const int MaxSalesPerType = 5;
    
    public int ItemId { get; init; }
    public int WorldId { get; init; }
    
    private readonly List<int> _nqPrices = new(MaxSalesPerType);
    private readonly List<int> _hqPrices = new(MaxSalesPerType);
    private readonly object _lock = new();
    
    public IReadOnlyList<int> NqPrices
    {
        get { lock (_lock) { return _nqPrices.ToList(); } }
    }
    
    public IReadOnlyList<int> HqPrices
    {
        get { lock (_lock) { return _hqPrices.ToList(); } }
    }
    
    public void AddPrice(int price, bool isHq)
    {
        lock (_lock)
        {
            var list = isHq ? _hqPrices : _nqPrices;
            list.Insert(0, price);
            if (list.Count > MaxSalesPerType)
            {
                list.RemoveAt(list.Count - 1);
            }
        }
    }
    
    /// <summary>
    /// Alias for AddPrice for backward compatibility.
    /// </summary>
    public void AddSale(int price, bool isHq) => AddPrice(price, isHq);
    
    public void SetPrices(IEnumerable<int> prices, bool isHq)
    {
        lock (_lock)
        {
            var list = isHq ? _hqPrices : _nqPrices;
            list.Clear();
            list.AddRange(prices.Take(MaxSalesPerType));
        }
    }
    
    public double MedianPriceNq => CalculateMedian(_nqPrices);
    public double MedianPriceHq => CalculateMedian(_hqPrices);
    public double AveragePriceNq => CalculateAverage(_nqPrices);
    public double AveragePriceHq => CalculateAverage(_hqPrices);
    public double StdDevNq => CalculateStdDev(_nqPrices);
    public double StdDevHq => CalculateStdDev(_hqPrices);
    
    private double CalculateMedian(List<int> prices)
    {
        int[] snapshot;
        lock (_lock)
        {
            if (prices.Count == 0) return 0;
            snapshot = prices.ToArray();
        }
        Array.Sort(snapshot);
        var mid = snapshot.Length / 2;
        return snapshot.Length % 2 == 0 
            ? (snapshot[mid - 1] + snapshot[mid]) / 2.0 
            : snapshot[mid];
    }
    
    private double CalculateAverage(List<int> prices)
    {
        lock (_lock)
        {
            if (prices.Count == 0) return 0;
            long sum = 0;
            foreach (var p in prices) sum += p;
            return (double)sum / prices.Count;
        }
    }
    
    private double CalculateStdDev(List<int> prices)
    {
        int[] snapshot;
        lock (_lock)
        {
            if (prices.Count < 2) return 0;
            snapshot = prices.ToArray();
        }
        double sum = 0;
        foreach (var p in snapshot) sum += p;
        var avg = sum / snapshot.Length;
        double sumSquares = 0;
        foreach (var p in snapshot) sumSquares += (p - avg) * (p - avg);
        return Math.Sqrt(sumSquares / (snapshot.Length - 1));
    }
}

/// <summary>
/// Summary statistics for the market data cache.
/// </summary>
public record MarketCacheStatistics
{
    public int TotalPriceEntries { get; init; }
    public int FreshEntries { get; init; }
    public int StaleEntries { get; init; }
    public int ExpiredEntries { get; init; }
    public int RecentSalesEntries { get; init; }
    public int LastSalePriceEntries { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public long StaleHits { get; init; }
    public long Evictions { get; init; }
    public double HitRate { get; init; }
    public DateTime? LastEvictionTime { get; init; }
}
