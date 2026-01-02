using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Centralized cache for market/price data with TTL support and staleness indicators.
/// Provides cache-first access to item prices with configurable freshness thresholds.
/// </summary>
public sealed class MarketDataCacheService : IService, IDisposable
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    
    // Main price cache: (itemId, worldId) -> cached price data
    private readonly ConcurrentDictionary<(int ItemId, int WorldId), MarketPriceCacheEntry> _priceCache = new();
    
    // Recent sales cache: (itemId, worldId) -> recent sale prices for outlier detection
    private readonly ConcurrentDictionary<(int ItemId, int WorldId), RecentSalesCacheEntry> _recentSalesCache = new();
    
    // Last sale price cache: (itemId, isHq) -> last known sale price (for spike detection)
    private readonly ConcurrentDictionary<(int ItemId, bool IsHq), int> _lastSalePriceCache = new();
    
    // Statistics
    private long _cacheHits;
    private long _cacheMisses;
    private long _staleHits;
    private long _evictions;
    private DateTime? _lastEvictionTime;
    
    // Configuration
    private const int DefaultTtlMinutes = 15;
    private const int DefaultStalenessThresholdMinutes = 60;
    private const int MaxCacheEntries = 50000; // Prevent unbounded growth
    
    public MarketDataCacheService(IPluginLog log, ConfigurationService configService)
    {
        _log = log;
        _configService = configService;
        LogService.Debug(LogCategory.Cache, "[MarketDataCache] Service initialized");
    }
    
    #region Public Properties - Statistics
    
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
        
        // Update last sale price cache for spike detection
        if (lastSaleNq > 0)
        {
            _lastSalePriceCache[(itemId, false)] = lastSaleNq;
        }
        if (lastSaleHq > 0)
        {
            _lastSalePriceCache[(itemId, true)] = lastSaleHq;
        }
        
        // Check if we need to evict old entries
        if (_priceCache.Count > MaxCacheEntries)
        {
            EvictOldestEntries(MaxCacheEntries / 10); // Evict 10%
        }
    }
    
    /// <summary>
    /// Updates only the min listing prices (from WebSocket listing events).
    /// </summary>
    public void UpdateMinPrices(int itemId, int worldId, int? minPriceNq, int? minPriceHq)
    {
        var key = (itemId, worldId);
        
        if (_priceCache.TryGetValue(key, out var existing))
        {
            // Merge with existing - keep lower prices
            var newNq = minPriceNq.HasValue && minPriceNq.Value > 0
                ? (existing.MinPriceNq > 0 ? Math.Min(existing.MinPriceNq, minPriceNq.Value) : minPriceNq.Value)
                : existing.MinPriceNq;
            var newHq = minPriceHq.HasValue && minPriceHq.Value > 0
                ? (existing.MinPriceHq > 0 ? Math.Min(existing.MinPriceHq, minPriceHq.Value) : minPriceHq.Value)
                : existing.MinPriceHq;
            
            existing.MinPriceNq = newNq;
            existing.MinPriceHq = newHq;
            existing.LastUpdated = DateTime.UtcNow;
            existing.Source = PriceSource.WebSocket;
        }
        else
        {
            // Create new entry
            SetPrice(itemId, worldId, 
                minPriceNq ?? 0, minPriceHq ?? 0, 
                source: PriceSource.WebSocket);
        }
    }
    
    /// <summary>
    /// Updates only the last sale prices (from WebSocket sale events).
    /// </summary>
    public void UpdateSalePrices(int itemId, int worldId, int? lastSaleNq, int? lastSaleHq)
    {
        var key = (itemId, worldId);
        
        if (_priceCache.TryGetValue(key, out var existing))
        {
            if (lastSaleNq.HasValue && lastSaleNq.Value > 0)
            {
                existing.LastSaleNq = lastSaleNq.Value;
                _lastSalePriceCache[(itemId, false)] = lastSaleNq.Value;
            }
            if (lastSaleHq.HasValue && lastSaleHq.Value > 0)
            {
                existing.LastSaleHq = lastSaleHq.Value;
                _lastSalePriceCache[(itemId, true)] = lastSaleHq.Value;
            }
            existing.LastUpdated = DateTime.UtcNow;
            existing.Source = PriceSource.WebSocket;
        }
        else
        {
            // Create new entry with sale prices
            SetPrice(itemId, worldId, 0, 0, 
                lastSaleNq ?? 0, lastSaleHq ?? 0, 
                source: PriceSource.WebSocket);
        }
    }
    
    /// <summary>
    /// Gets all cached prices for a specific item across all worlds.
    /// </summary>
    public IReadOnlyDictionary<int, MarketPriceCacheEntry> GetPricesForItem(int itemId)
    {
        var result = new Dictionary<int, MarketPriceCacheEntry>();
        foreach (var kvp in _priceCache)
        {
            if (kvp.Key.ItemId == itemId)
            {
                result[kvp.Key.WorldId] = kvp.Value;
            }
        }
        return result;
    }
    
    /// <summary>
    /// Gets all cached prices for a specific world.
    /// </summary>
    public IReadOnlyDictionary<int, MarketPriceCacheEntry> GetPricesForWorld(int worldId)
    {
        var result = new Dictionary<int, MarketPriceCacheEntry>();
        foreach (var kvp in _priceCache)
        {
            if (kvp.Key.WorldId == worldId)
            {
                result[kvp.Key.ItemId] = kvp.Value;
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
        return _priceCache.TryRemove((itemId, worldId), out _);
    }
    
    /// <summary>
    /// Clears all price cache entries.
    /// </summary>
    public void ClearPriceCache()
    {
        _priceCache.Clear();
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
    public int MinPriceNq { get; set; }
    public int MinPriceHq { get; set; }
    public int LastSaleNq { get; set; }
    public int LastSaleHq { get; set; }
    public DateTime LastUpdated { get; set; }
    public PriceSource Source { get; set; }
    
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
        lock (_lock)
        {
            if (prices.Count == 0) return 0;
            var sorted = prices.OrderBy(p => p).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 
                ? (sorted[mid - 1] + sorted[mid]) / 2.0 
                : sorted[mid];
        }
    }
    
    private double CalculateAverage(List<int> prices)
    {
        lock (_lock)
        {
            return prices.Count > 0 ? prices.Average() : 0;
        }
    }
    
    private double CalculateStdDev(List<int> prices)
    {
        lock (_lock)
        {
            if (prices.Count < 2) return 0;
            var avg = prices.Average();
            var sumSquares = prices.Sum(p => (p - avg) * (p - avg));
            return Math.Sqrt(sumSquares / (prices.Count - 1));
        }
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
