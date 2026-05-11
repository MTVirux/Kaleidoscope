using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using Kaleidoscope.Services.Database;
using Kaleidoscope.Services.Resources.Adapters;
using OtterGui.Services;
using System.Collections.Concurrent;
using Kaleidoscope.Services.Characters;

namespace Kaleidoscope.Services;

/// <summary>
/// High-performance in-memory cache for time-series data.
/// Provides instant read access for UI components while background DB writes continue.
/// </summary>
/// <remarks>
/// Key design principles:
/// 1. All reads come from cache (no DB queries during graph rendering)
/// 2. Writes update cache immediately, then queue DB write
/// 3. Cache is populated from DB on startup/first access
/// 4. Memory bounded via sliding window and LRU eviction
/// </remarks>
public sealed class TimeSeriesCacheService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly CharacterDataCacheService _characterDataCache;
    private readonly Kaleidoscope.Services.Resources.ResourceStore? _resourceStore;

    private readonly ConcurrentDictionary<CacheKey, TimeSeriesCache> _cache = new();

    private readonly ConcurrentDictionary<string, HashSet<ulong>> _availableCharacters = new();
    private readonly object _availableCharactersLock = new();

    private readonly object _inventoryValueCacheLock = new();
    private List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)>? _inventoryValueCache;
    private long _inventoryValueCacheRecordCount;
    private long? _inventoryValueCacheMaxTimestamp;
    private DateTime _inventoryValueCacheTime = DateTime.MinValue;

    private long _cacheHits;
    private long _cacheMisses;
    private long _version;

    public event Action<string, ulong>? OnCacheUpdated;

    /// <summary>
    /// Monotonically increasing version counter. Incremented on every cache mutation.
    /// Consumers can compare against a stored version to detect changes without polling.
    /// </summary>
    public long Version => Volatile.Read(ref _version);

    public TimeSeriesCacheConfig CacheConfig => _configService.Config.TimeSeriesCacheConfig;
    public long CacheHits => _cacheHits;
    public long CacheMisses => _cacheMisses;
    public int CachedSeriesCount => _cache.Count;
    public long TotalCachedPoints => _cache.Values.Sum(c => c.PointCount);

    public TimeSeriesCacheService(IPluginLog log, ConfigurationService configService, KaleidoscopeDbService dbService, CharacterDataCacheService characterDataCache,
        Kaleidoscope.Services.Resources.ResourceStore? resourceStore = null)
    {
        _log = log;
        _configService = configService;
        _dbService = dbService;
        _characterDataCache = characterDataCache;
        _resourceStore = resourceStore;
        LogService.Debug(LogCategory.Cache, "[TimeSeriesCacheService] Initialized");
    }

    /// <summary>
    /// Gets cached points for a variable/character combination.
    /// Returns empty list if not cached (caller should populate from DB).
    /// </summary>
    public IReadOnlyList<(DateTime timestamp, long value)> GetCachedPoints(string variable, ulong characterId)
    {
        var key = new CacheKey(variable, characterId);
        if (_cache.TryGetValue(key, out var cache))
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose(LogCategory.Cache, $"[Cache HIT] {variable}:{characterId}");
            return cache.GetPoints();
        }

        Interlocked.Increment(ref _cacheMisses);
        LogService.Verbose(LogCategory.Cache, $"[Cache MISS] {variable}:{characterId}");
        return Array.Empty<(DateTime, long)>();
    }

    public IReadOnlyList<(DateTime timestamp, long value)> GetCachedPoints(string variable, ulong characterId, DateTime since)
    {
        var key = new CacheKey(variable, characterId);
        if (_cache.TryGetValue(key, out var cache))
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose(LogCategory.Cache, $"[Cache HIT] {variable}:{characterId} (since {since:HH:mm:ss})");
            return cache.GetPointsSince(since);
        }

        Interlocked.Increment(ref _cacheMisses);
        LogService.Verbose(LogCategory.Cache, $"[Cache MISS] {variable}:{characterId} (since {since:HH:mm:ss})");
        return Array.Empty<(DateTime, long)>();
    }

    public (DateTime timestamp, long value)? GetLastCachedPoint(string variable, ulong characterId)
    {
        var key = new CacheKey(variable, characterId);
        if (_cache.TryGetValue(key, out var cache))
        {
            return cache.GetLastPoint();
        }
        return null;
    }

    public IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> GetAllCachedPoints(string variable)
    {
        return GetAllCachedPoints(variable, null);
    }

    /// <summary>
    /// Gets all cached points across all characters for a variable, optionally filtered by time.
    /// </summary>
    /// <param name="variable">The variable name to query.</param>
    /// <param name="since">Optional: only return points after this timestamp.</param>
    /// <returns>List of (characterId, timestamp, value) tuples sorted by timestamp.</returns>
    public IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> GetAllCachedPoints(string variable, DateTime? since)
    {
        var result = new List<(ulong, DateTime, long)>();
        var foundAny = false;

        foreach (var kvp in _cache)
        {
            if (kvp.Key.Variable != variable) continue;
            foundAny = true;

            var characterId = kvp.Key.CharacterId;
            var points = since.HasValue
                ? kvp.Value.GetPointsSince(since.Value)
                : kvp.Value.GetPoints();
            foreach (var (ts, val) in points)
            {
                result.Add((characterId, ts, val));
            }
        }

        if (foundAny)
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose(LogCategory.Cache, $"[Cache HIT] GetAllCachedPoints({variable}) - {result.Count} points");
        }
        else
        {
            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose(LogCategory.Cache, $"[Cache MISS] GetAllCachedPoints({variable})");
        }

        result.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return result;
    }

    /// <summary>
    /// Gets all cached character series for a variable (for multi-line graphs).
    /// Names are automatically disambiguated when multiple characters have the same display name.
    /// </summary>
    public IReadOnlyList<(ulong characterId, string name, IReadOnlyList<(DateTime ts, long value)> points)> GetAllCachedCharacterSeries(string variable, DateTime? cutoffTime = null)
    {
        var result = new List<(ulong, string, IReadOnlyList<(DateTime, long)>)>();
        var foundAny = false;

        var characterNames = new Dictionary<ulong, string>();
        var characterPoints = new Dictionary<ulong, IReadOnlyList<(DateTime, long)>>();
        
        foreach (var kvp in _cache)
        {
            if (kvp.Key.Variable != variable) continue;
            foundAny = true;

            var characterId = kvp.Key.CharacterId;
            var name = GetFormattedCharacterName(characterId) ?? $"...{characterId % 1_000_000:D6}";
            var points = cutoffTime.HasValue
                ? kvp.Value.GetPointsSince(cutoffTime.Value)
                : kvp.Value.GetPoints();

            if (points.Count > 0)
            {
                characterNames[characterId] = name;
                characterPoints[characterId] = points;
            }
        }

        var nameCounts = characterNames.Values.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        
        foreach (var (characterId, baseName) in characterNames)
        {
            var displayName = baseName;
            
            if (nameCounts.Contains(baseName))
            {
                // Append last 4 digits of character ID for disambiguation
                displayName = $"{baseName} (#{characterId % 10000:D4})";
            }
            
            result.Add((characterId, displayName, characterPoints[characterId]));
        }

        if (foundAny)
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose(LogCategory.Cache, $"[Cache HIT] GetAllCachedCharacterSeries({variable}) - {result.Count} series");
        }
        else
        {
            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose(LogCategory.Cache, $"[Cache MISS] GetAllCachedCharacterSeries({variable})");
        }

        return result;
    }

    public bool IsCached(string variable, ulong characterId)
    {
        return _cache.ContainsKey(new CacheKey(variable, characterId));
    }

    public IReadOnlyList<ulong> GetAvailableCharacters(string variable)
    {
        lock (_availableCharactersLock)
        {
            if (_availableCharacters.TryGetValue(variable, out var chars))
            {
                return chars.ToList();
            }
            return Array.Empty<ulong>();
        }
    }

    /// <summary>
    /// Gets a character's display name from cache (display_name if set, otherwise game name).
    /// </summary>
    public string? GetCharacterName(ulong characterId)
    {
        return _characterDataCache.GetCharacterName(characterId);
    }

    public uint? GetCharacterTimeSeriesColor(ulong characterId)
    {
        return _characterDataCache.GetCharacterTimeSeriesColor(characterId);
    }

    /// <summary>
    /// Gets a formatted character name based on the current name format setting.
    /// Returns display_name if set (unformatted), otherwise formats the game name.
    /// </summary>
    public string? GetFormattedCharacterName(ulong characterId)
    {
        return _characterDataCache.GetFormattedCharacterName(characterId);
    }

    /// <summary>
    /// Gets disambiguated display names for a set of character IDs.
    /// When multiple characters have the same formatted name, appends a short identifier.
    /// </summary>
    /// <param name="characterIds">The character IDs to get names for.</param>
    /// <returns>Dictionary mapping character ID to disambiguated display name.</returns>
    public Dictionary<ulong, string> GetDisambiguatedNames(IEnumerable<ulong> characterIds)
    {
        return _characterDataCache.GetDisambiguatedNames(characterIds);
    }

    /// <summary>
    /// Gets the latest cached value for each character for a given variable.
    /// Special cases for variables that require parent-owner aggregation or multi-itemId summing
    /// are dispatched directly to resources-table queries. Everything else falls through to the
    /// legacy translator path via resource_history.
    /// </summary>
    /// <returns>Dictionary of characterId -> latest value.</returns>
    public Dictionary<ulong, long> GetLatestValuesForVariable(string variable)
    {
        // Special cases: these variables cannot be resolved through the single-(item,container,owner)
        // translator path because they either span multiple owners (retainer gil → parent_owner_id)
        // or multiple item IDs (crystals). Query the resources table directly.
        switch (variable)
        {
            case "Gil":
                // In-memory store — sub-frame latency vs. ~1s DB batching.
                if (_resourceStore != null)
                    return _resourceStore.GetPerOwnerSum(Kaleidoscope.Services.Resources.ResourceCatalog.GilItemId, Kaleidoscope.Models.Resources.OwnerKind.Player);
                return _dbService.GetItemSumPerCharacterPlayerOnly(
                    Kaleidoscope.Services.Resources.ResourceCatalog.GilItemId,
                    (int)Kaleidoscope.Models.Resources.Container.SpecialPlayer);
            case "RetainerGil":
                return _dbService.GetRetainerGilPerCharacter();
            case "FreeCompanyGil":
                // FC gil is per-FC, not per-character; the active-character live path handles it.
                return new Dictionary<ulong, long>();
            case "MGP":
                if (_resourceStore != null)
                    return _resourceStore.GetPerOwnerSum(Kaleidoscope.Services.Resources.ResourceCatalog.MGPItemId, Kaleidoscope.Models.Resources.OwnerKind.Player);
                return _dbService.GetItemSumPerCharacterPlayerOnly(
                    Kaleidoscope.Services.Resources.ResourceCatalog.MGPItemId,
                    (int)Kaleidoscope.Models.Resources.Container.SpecialPlayer);
            case "WolfMarks":
                if (_resourceStore != null)
                    return _resourceStore.GetPerOwnerSum(Kaleidoscope.Services.Resources.ResourceCatalog.WolfMarksItemId, Kaleidoscope.Models.Resources.OwnerKind.Player);
                return _dbService.GetItemSumPerCharacterPlayerOnly(
                    Kaleidoscope.Services.Resources.ResourceCatalog.WolfMarksItemId,
                    (int)Kaleidoscope.Models.Resources.Container.SpecialPlayer);
            case "AlliedSeals":
                if (_resourceStore != null)
                    return _resourceStore.GetPerOwnerSum(Kaleidoscope.Services.Resources.ResourceCatalog.AlliedSealsItemId, Kaleidoscope.Models.Resources.OwnerKind.Player);
                return _dbService.GetItemSumPerCharacterPlayerOnly(
                    Kaleidoscope.Services.Resources.ResourceCatalog.AlliedSealsItemId,
                    (int)Kaleidoscope.Models.Resources.Container.SpecialPlayer);
            case "FireCrystals":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(new uint[] { 2, 8, 14 });
            case "IceCrystals":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(new uint[] { 3, 9, 15 });
            case "WindCrystals":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(new uint[] { 4, 10, 16 });
            case "EarthCrystals":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(new uint[] { 5, 11, 17 });
            case "LightningCrystals":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(new uint[] { 6, 12, 18 });
            case "WaterCrystals":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(new uint[] { 7, 13, 19 });
            case "CrystalsTotal":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(
                    Enumerable.Range(2, 18).Select(i => (uint)i));
            case "Ventures":
                return _dbService.GetItemSumPerCharacterIncludingRetainers(new uint[] { 21072 });
        }

        // Existing translator-based path for everything else (tomestones, scrips, GC seals, etc.)
        return GetLatestValuesForVariableViaResources(variable);
    }

    private Dictionary<ulong, long> GetLatestValuesForVariableViaResources(string variable)
    {
        var result = new Dictionary<ulong, long>();
        var pairs = _dbService.GetSeriesByVariablePrefixSuffix(variable, null);
        foreach (var (v, charId) in pairs)
        {
            if (v != variable) continue;
            var spec = LegacyVariableTranslator.Translate(variable, charId);
            if (spec == null) continue;
            var latest = _dbService.GetLatestHistoryValue(spec.Value.ItemId, spec.Value.OwnerId, (int)spec.Value.Container);
            if (latest.HasValue) result[charId] = latest.Value;
        }
        return result;
    }

    /// <summary>
    /// Gets all points for a variable from resource_history, grouped by variable name.
    /// Compatible with DbService.GetAllPointsBatch signature.
    /// </summary>
    /// <param name="variable">The variable name to query.</param>
    /// <param name="since">Only return points after this timestamp. If null, returns all points.</param>
    /// <returns>Dictionary with variable name as key and list of points as value.</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetAllPointsBatch(string variable, DateTime? since)
    {
        return GetPointsBatchViaResources(variable, suffix: null, since);
    }

    /// <summary>
    /// Gets all points for variables matching a prefix+suffix pattern from resource_history.
    /// Compatible with DbService.GetPointsBatchWithSuffix signature.
    /// </summary>
    /// <param name="prefix">Variable name prefix (e.g., "ItemRetainerX_").</param>
    /// <param name="suffix">Variable name suffix (e.g., "_12345").</param>
    /// <param name="since">Only return points after this timestamp. If null, returns all points.</param>
    /// <returns>Dictionary with variable name as key and list of points as value.</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPointsBatchWithSuffix(string prefix, string suffix, DateTime? since)
    {
        return GetPointsBatchViaResources(prefix, suffix, since);
    }

    private Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPointsBatchViaResources(
        string prefix, string? suffix, DateTime? since)
    {
        var result = new Dictionary<string, List<(ulong, DateTime, long)>>();
        var pairs = _dbService.GetSeriesByVariablePrefixSuffix(prefix, suffix);

        foreach (var (variable, charId) in pairs)
        {
            var spec = LegacyVariableTranslator.Translate(variable, charId);
            if (spec == null) continue;

            var points = _dbService.GetHistoryPoints(spec.Value.ItemId, spec.Value.OwnerId, (int)spec.Value.Container, since);
            if (points.Count == 0) continue;

            if (!result.TryGetValue(variable, out var list))
            {
                list = new List<(ulong, DateTime, long)>();
                result[variable] = list;
            }

            foreach (var (ts, val) in points)
                list.Add((charId, new DateTime(ts, DateTimeKind.Utc), val));
        }

        return result;
    }

    public IReadOnlyList<string> GetVariablesWithPrefix(string prefix)
    {
        return _cache.Keys
            .Where(k => k.Variable.StartsWith(prefix))
            .Select(k => k.Variable)
            .Distinct()
            .ToList();
    }

    public bool HasDataForVariable(string variable)
    {
        return _cache.Keys.Any(k => k.Variable == variable);
    }

    /// <summary>
    /// Adds or updates a single point in the cache.
    /// Call this immediately when new data is received (before DB write).
    /// </summary>
    /// <param name="variable">The variable name (e.g., "Gil", "TomestonePoetics").</param>
    /// <param name="characterId">The character's content ID.</param>
    /// <param name="value">The new value.</param>
    /// <param name="timestamp">Optional timestamp (defaults to UTC now).</param>
    /// <returns>True if this is a new value (different from last cached value).</returns>
    public bool AddPoint(string variable, ulong characterId, long value, DateTime? timestamp = null)
    {
        var key = new CacheKey(variable, characterId);
        var ts = timestamp ?? DateTime.UtcNow;

        var cache = _cache.GetOrAdd(key, _ => new TimeSeriesCache(CacheConfig.MaxPointsPerSeries));

        var lastPoint = cache.GetLastPoint();
        if (lastPoint.HasValue && lastPoint.Value.value == value)
        {
            return false;
        }

        cache.AddPoint(ts, value);
        Interlocked.Increment(ref _version);

        lock (_availableCharactersLock)
        {
            if (!_availableCharacters.TryGetValue(variable, out var chars))
            {
                chars = new HashSet<ulong>();
                _availableCharacters[variable] = chars;
            }
            chars.Add(characterId);
        }

        OnCacheUpdated?.Invoke(variable, characterId);

        return true;
    }

    /// <summary>
    /// Populates the cache from database data.
    /// Call this during initialization or when cache needs to be refreshed.
    /// </summary>
    public void PopulateFromDatabase(string variable, ulong characterId, IEnumerable<(DateTime timestamp, long value)> points)
    {
        var key = new CacheKey(variable, characterId);
        var cache = _cache.GetOrAdd(key, _ => new TimeSeriesCache(CacheConfig.MaxPointsPerSeries));

        cache.Clear();
        foreach (var (ts, val) in points)
        {
            cache.AddPoint(ts, val);
        }
        Interlocked.Increment(ref _version);

        lock (_availableCharactersLock)
        {
            if (!_availableCharacters.TryGetValue(variable, out var chars))
            {
                chars = new HashSet<ulong>();
                _availableCharacters[variable] = chars;
            }
            chars.Add(characterId);
        }
    }

    public void PopulateAvailableCharacters(string variable, IEnumerable<ulong> characterIds)
    {
        lock (_availableCharactersLock)
        {
            if (!_availableCharacters.TryGetValue(variable, out var chars))
            {
                chars = new HashSet<ulong>();
                _availableCharacters[variable] = chars;
            }
            foreach (var cid in characterIds)
            {
                chars.Add(cid);
            }
        }
    }

    public void SetCharacterName(ulong characterId, string name)
    {
        _characterDataCache.SetCharacterName(characterId, name);
    }

    public void SetCharacterDisplayName(ulong characterId, string? displayName)
    {
        _characterDataCache.SetCharacterDisplayName(characterId, displayName);
    }

    public void SetCharacterTimeSeriesColor(ulong characterId, uint? color)
    {
        _characterDataCache.SetCharacterTimeSeriesColor(characterId, color);
    }

    public void PopulateCharacterNames(IEnumerable<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> names)
    {
        _characterDataCache.PopulateCharacterData(names);
    }

    public void PopulateCharacterNamesSimple(IEnumerable<(ulong characterId, string? name)> names)
    {
        _characterDataCache.PopulateCharacterNamesSimple(names);
    }

    public void Invalidate(string variable, ulong characterId)
    {
        var key = new CacheKey(variable, characterId);
        if (_cache.TryRemove(key, out _))
            Interlocked.Increment(ref _version);
    }

    public void InvalidateVariable(string variable)
    {
        var keysToRemove = _cache.Keys.Where(k => k.Variable == variable).ToList();
        var removed = false;
        foreach (var key in keysToRemove)
        {
            removed |= _cache.TryRemove(key, out _);
        }

        lock (_availableCharactersLock)
        {
            _availableCharacters.TryRemove(variable, out _);
        }

        if (removed)
            Interlocked.Increment(ref _version);
    }

    public void ClearAll()
    {
        _cache.Clear();
        lock (_availableCharactersLock)
        {
            _availableCharacters.Clear();
        }
        _cacheHits = 0;
        _cacheMisses = 0;
        Interlocked.Increment(ref _version);
    }

    public void RemoveCharacter(ulong characterId)
    {
        var keysToRemove = _cache.Keys.Where(k => k.CharacterId == characterId).ToList();
        var removed = false;
        foreach (var key in keysToRemove)
        {
            removed |= _cache.TryRemove(key, out _);
        }

        _characterDataCache.RemoveCharacter(characterId);

        lock (_availableCharactersLock)
        {
            foreach (var chars in _availableCharacters.Values)
            {
                chars.Remove(characterId);
            }
        }

        if (removed)
            Interlocked.Increment(ref _version);
    }

    /// <summary>
    /// Trims old data from all cached series based on the configured time window.
    /// Call this periodically to prevent unbounded memory growth.
    /// </summary>
    public void TrimOldData()
    {
        var cutoff = DateTime.UtcNow.AddHours(-CacheConfig.MaxCacheHours);

        foreach (var cache in _cache.Values)
        {
            cache.TrimBefore(cutoff);
        }

        var emptyKeys = _cache.Where(kvp => kvp.Value.PointCount == 0).Select(kvp => kvp.Key).ToList();
        foreach (var key in emptyKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            SeriesCount = _cache.Count,
            TotalPoints = TotalCachedPoints,
            CharacterCount = _characterDataCache.CachedCharacterCount,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            HitRate = _cacheHits + _cacheMisses > 0
                ? (double)_cacheHits / (_cacheHits + _cacheMisses)
                : 0.0
        };
    }

    /// <summary>
    /// Gets or refreshes the inventory value history cache.
    /// Returns cached data if still valid, otherwise returns null indicating caller should refresh.
    /// </summary>
    /// <param name="dbRecordCount">Current DB record count for change detection</param>
    /// <param name="dbMaxTimestamp">Current DB max timestamp for change detection</param>
    /// <returns>Cached data if valid, null if refresh needed</returns>
    public List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)>? GetInventoryValueCache(
        long dbRecordCount, long? dbMaxTimestamp)
    {
        lock (_inventoryValueCacheLock)
        {
            // Check if cache is valid
            if (_inventoryValueCache != null &&
                _inventoryValueCacheRecordCount == dbRecordCount &&
                _inventoryValueCacheMaxTimestamp == dbMaxTimestamp)
            {
                Interlocked.Increment(ref _cacheHits);
                LogService.Verbose(LogCategory.Cache, $"[Cache HIT] InventoryValueHistory - {_inventoryValueCache.Count} records");
                return _inventoryValueCache;
            }

            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose(LogCategory.Cache, "[Cache MISS] InventoryValueHistory");
            return null;
        }
    }

    public void SetInventoryValueCache(
        List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> data,
        long dbRecordCount, long? dbMaxTimestamp)
    {
        lock (_inventoryValueCacheLock)
        {
            _inventoryValueCache = data;
            _inventoryValueCacheRecordCount = dbRecordCount;
            _inventoryValueCacheMaxTimestamp = dbMaxTimestamp;
            _inventoryValueCacheTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Updates the inventory value history cache with fresh data, calculating stats automatically.
    /// Used by background thread when populating cache from DB.
    /// </summary>
    public void SetInventoryValueCache(
        List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> data)
    {
        // Calculate stats from the data
        long recordCount = data.Count;
        long? maxTimestamp = null;
        
        if (data.Count > 0)
        {
            var maxTs = data.Max(d => d.Timestamp);
            maxTimestamp = maxTs.Ticks;
        }
        
        lock (_inventoryValueCacheLock)
        {
            _inventoryValueCache = data;
            _inventoryValueCacheRecordCount = recordCount;
            _inventoryValueCacheMaxTimestamp = maxTimestamp;
            _inventoryValueCacheTime = DateTime.UtcNow;
        }
        
        LogService.Debug(LogCategory.Cache, $"[TimeSeriesCacheService] Inventory value cache populated with {recordCount} records");
    }

    /// <summary>
    /// Invalidates the inventory value history cache.
    /// Call this when data is known to have changed.
    /// </summary>
    public void InvalidateInventoryValueCache()
    {
        lock (_inventoryValueCacheLock)
        {
            _inventoryValueCache = null;
            _inventoryValueCacheRecordCount = 0;
            _inventoryValueCacheMaxTimestamp = null;
        }
        Interlocked.Increment(ref _version);
        
        // Fire event to notify UI that data has changed
        OnInventoryValueCacheInvalidated?.Invoke();
    }
    
    /// <summary>
    /// Event fired when inventory value cache is invalidated.
    /// UI components can subscribe to trigger refresh.
    /// </summary>
    public event Action? OnInventoryValueCacheInvalidated;

    /// <summary>
    /// Gets the cached inventory value history data.
    /// Returns null if cache is empty (caller should wait for background population).
    /// This method NEVER hits the database.
    /// </summary>
    public List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)>? GetInventoryValueHistoryFromCache()
    {
        lock (_inventoryValueCacheLock)
        {
            if (_inventoryValueCache != null)
            {
                Interlocked.Increment(ref _cacheHits);
                LogService.Verbose(LogCategory.Cache, $"[Cache HIT] InventoryValueHistory - {_inventoryValueCache.Count} records");
                return _inventoryValueCache;
            }

            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose(LogCategory.Cache, "[Cache MISS] InventoryValueHistory - cache empty");
            return null;
        }
    }
    
    /// <summary>
    /// Gets the cached inventory value stats (record count and max timestamp).
    /// Returns (0, null) if cache is empty.
    /// This method NEVER hits the database.
    /// </summary>
    public (long recordCount, long? maxTimestamp) GetInventoryValueStatsFromCache()
    {
        lock (_inventoryValueCacheLock)
        {
            return (_inventoryValueCacheRecordCount, _inventoryValueCacheMaxTimestamp);
        }
    }
    
    public bool HasInventoryValueCache
    {
        get
        {
            lock (_inventoryValueCacheLock)
            {
                return _inventoryValueCache != null;
            }
        }
    }

    public void Dispose()
    {
        ClearAll();
    }

    private readonly record struct CacheKey(string Variable, ulong CharacterId);
}

/// <summary>
/// Thread-safe cache for a single time series (one variable for one character).
/// </summary>
internal sealed class TimeSeriesCache
{
    private readonly object _lock = new();
    private readonly List<(DateTime timestamp, long value)> _points = new();
    private readonly int _maxPoints;

    public int PointCount
    {
        get
        {
            lock (_lock)
            {
                return _points.Count;
            }
        }
    }

    public TimeSeriesCache(int maxPoints)
    {
        _maxPoints = maxPoints;
    }

    public void AddPoint(DateTime timestamp, long value)
    {
        lock (_lock)
        {
            _points.Add((timestamp, value));

            if (_points.Count > _maxPoints)
            {
                var removeCount = _points.Count - _maxPoints;
                _points.RemoveRange(0, removeCount);
            }
        }
    }

    public IReadOnlyList<(DateTime timestamp, long value)> GetPoints()
    {
        lock (_lock)
        {
            return _points.ToList();
        }
    }

    public IReadOnlyList<(DateTime timestamp, long value)> GetPointsSince(DateTime since)
    {
        lock (_lock)
        {
            return _points.Where(p => p.timestamp >= since).ToList();
        }
    }

    public (DateTime timestamp, long value)? GetLastPoint()
    {
        lock (_lock)
        {
            return _points.Count > 0 ? _points[^1] : null;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _points.Clear();
        }
    }

    public void TrimBefore(DateTime cutoff)
    {
        lock (_lock)
        {
            var removeCount = 0;
            for (var i = 0; i < _points.Count; i++)
            {
                if (_points[i].timestamp >= cutoff) break;
                removeCount++;
            }
            if (removeCount > 0)
            {
                _points.RemoveRange(0, removeCount);
            }
        }
    }
}

public sealed class TimeSeriesCacheConfig
{
    /// <summary>
    /// Maximum number of points to cache per series.
    /// Default: 10000 points (~10KB per series).
    /// </summary>
    public int MaxPointsPerSeries { get; set; } = 10000;

    /// <summary>
    /// Maximum hours of data to keep in cache.
    /// Older data is trimmed during maintenance.
    /// Default: 168 hours (7 days).
    /// </summary>
    public int MaxCacheHours { get; set; } = 168;

    public bool PrePopulateOnStartup { get; set; } = true;

    /// <summary>
    /// Hours of historical data to load from DB on startup.
    /// Only applies when PrePopulateOnStartup is true.
    /// Default: 24 hours.
    /// </summary>
    public int StartupLoadHours { get; set; } = 24;
}

public readonly struct CacheStatistics
{
    public int SeriesCount { get; init; }
    public long TotalPoints { get; init; }
    public int CharacterCount { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public double HitRate { get; init; }
}
