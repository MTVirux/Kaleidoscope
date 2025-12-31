using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;
using System.Collections.Concurrent;

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
    private readonly CharacterDataCacheService _characterDataCache;

    // Main cache: (variable, characterId) -> cached time series
    private readonly ConcurrentDictionary<CacheKey, TimeSeriesCache> _cache = new();

    // Available characters per variable
    private readonly ConcurrentDictionary<string, HashSet<ulong>> _availableCharacters = new();
    private readonly object _availableCharactersLock = new();

    // Inventory value history cache
    private readonly object _inventoryValueCacheLock = new();
    private List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)>? _inventoryValueCache;
    private long _inventoryValueCacheRecordCount;
    private long? _inventoryValueCacheMaxTimestamp;
    private DateTime _inventoryValueCacheTime = DateTime.MinValue;

    // Cache statistics for monitoring
    private long _cacheHits;
    private long _cacheMisses;

    // Event for notifying subscribers when cache is updated
    public event Action<string, ulong>? OnCacheUpdated;

    /// <summary>
    /// Gets the cache configuration.
    /// </summary>
    public TimeSeriesCacheConfig CacheConfig => _configService.Config.TimeSeriesCacheConfig;

    /// <summary>
    /// Gets cache hit count for diagnostics.
    /// </summary>
    public long CacheHits => _cacheHits;

    /// <summary>
    /// Gets cache miss count for diagnostics.
    /// </summary>
    public long CacheMisses => _cacheMisses;

    /// <summary>
    /// Gets total number of cached series.
    /// </summary>
    public int CachedSeriesCount => _cache.Count;

    /// <summary>
    /// Gets total cached points across all series.
    /// </summary>
    public long TotalCachedPoints => _cache.Values.Sum(c => c.PointCount);

    public TimeSeriesCacheService(IPluginLog log, ConfigurationService configService, CharacterDataCacheService characterDataCache)
    {
        _log = log;
        _configService = configService;
        _characterDataCache = characterDataCache;
        _log.Debug("[TimeSeriesCacheService] Initialized");
    }

    #region Cache Read Operations

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
            LogService.Verbose($"[Cache HIT] {variable}:{characterId}");
            return cache.GetPoints();
        }

        Interlocked.Increment(ref _cacheMisses);
        LogService.Verbose($"[Cache MISS] {variable}:{characterId}");
        return Array.Empty<(DateTime, long)>();
    }

    /// <summary>
    /// Gets cached points filtered by time range.
    /// </summary>
    public IReadOnlyList<(DateTime timestamp, long value)> GetCachedPoints(string variable, ulong characterId, DateTime since)
    {
        var key = new CacheKey(variable, characterId);
        if (_cache.TryGetValue(key, out var cache))
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose($"[Cache HIT] {variable}:{characterId} (since {since:HH:mm:ss})");
            return cache.GetPointsSince(since);
        }

        Interlocked.Increment(ref _cacheMisses);
        LogService.Verbose($"[Cache MISS] {variable}:{characterId} (since {since:HH:mm:ss})");
        return Array.Empty<(DateTime, long)>();
    }

    /// <summary>
    /// Gets the last cached value for a variable/character combination.
    /// Returns null if not cached.
    /// </summary>
    public (DateTime timestamp, long value)? GetLastCachedPoint(string variable, ulong characterId)
    {
        var key = new CacheKey(variable, characterId);
        if (_cache.TryGetValue(key, out var cache))
        {
            return cache.GetLastPoint();
        }
        return null;
    }

    /// <summary>
    /// Gets all cached points across all characters for a variable.
    /// </summary>
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

        // Count as hit if we found any matching series, miss otherwise
        if (foundAny)
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose($"[Cache HIT] GetAllCachedPoints({variable}) - {result.Count} points");
        }
        else
        {
            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose($"[Cache MISS] GetAllCachedPoints({variable})");
        }

        // Sort by timestamp
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

        // First pass: collect all character IDs and their base names
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

        // Second pass: detect name collisions and disambiguate
        var nameCounts = characterNames.Values.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        
        foreach (var (characterId, baseName) in characterNames)
        {
            var displayName = baseName;
            
            // If this name appears multiple times, append a short identifier
            if (nameCounts.Contains(baseName))
            {
                // Append last 4 digits of character ID for disambiguation
                displayName = $"{baseName} (#{characterId % 10000:D4})";
            }
            
            result.Add((characterId, displayName, characterPoints[characterId]));
        }

        // Count as hit if we found any matching series, miss otherwise
        if (foundAny)
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose($"[Cache HIT] GetAllCachedCharacterSeries({variable}) - {result.Count} series");
        }
        else
        {
            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose($"[Cache MISS] GetAllCachedCharacterSeries({variable})");
        }

        return result;
    }

    /// <summary>
    /// Checks if a variable/character combination is cached.
    /// </summary>
    public bool IsCached(string variable, ulong characterId)
    {
        return _cache.ContainsKey(new CacheKey(variable, characterId));
    }

    /// <summary>
    /// Gets all available characters for a variable.
    /// </summary>
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

    /// <summary>
    /// Gets a character's game name from cache.
    /// </summary>
    public string? GetCharacterGameName(ulong characterId)
    {
        return _characterDataCache.GetCharacterGameName(characterId);
    }

    /// <summary>
    /// Gets a character's custom display name from cache.
    /// </summary>
    public string? GetCharacterDisplayName(ulong characterId)
    {
        return _characterDataCache.GetCharacterDisplayName(characterId);
    }

    /// <summary>
    /// Gets a character's time series color from cache.
    /// </summary>
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
    /// Formats a name according to the specified format.
    /// </summary>
    public static string? FormatName(string? fullName, CharacterNameFormat format)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return fullName;

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return fullName;

        return format switch
        {
            CharacterNameFormat.FullName => fullName,
            CharacterNameFormat.FirstNameOnly => parts[0],
            CharacterNameFormat.LastNameOnly => parts.Length > 1 ? parts[^1] : parts[0],
            CharacterNameFormat.Initials => string.Join(".", parts.Select(p => p.Length > 0 ? p[0].ToString().ToUpperInvariant() : "")) + ".",
            _ => fullName
        };
    }

    #region Cache-First Batch Read Operations (Phase 3)

    /// <summary>
    /// Gets the latest cached value for each character for a given variable.
    /// Cache-first: returns only cached data, no DB fallback.
    /// </summary>
    /// <returns>Dictionary of characterId -> latest value.</returns>
    public Dictionary<ulong, long> GetLatestValuesForVariable(string variable)
    {
        var result = new Dictionary<ulong, long>();
        var foundAny = false;

        foreach (var kvp in _cache)
        {
            if (kvp.Key.Variable != variable) continue;
            foundAny = true;

            var lastPoint = kvp.Value.GetLastPoint();
            if (lastPoint.HasValue)
            {
                result[kvp.Key.CharacterId] = lastPoint.Value.value;
            }
        }

        if (foundAny)
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose($"[Cache HIT] GetLatestValuesForVariable({variable}) - {result.Count} characters");
        }
        else
        {
            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose($"[Cache MISS] GetLatestValuesForVariable({variable})");
        }

        return result;
    }

    /// <summary>
    /// Gets all cached points for a variable, grouped by variable name.
    /// Cache-first: returns only cached data, no DB fallback.
    /// Compatible with DbService.GetAllPointsBatch signature.
    /// </summary>
    /// <param name="variable">The variable name to query.</param>
    /// <param name="since">Only return points after this timestamp. If null, returns all points.</param>
    /// <returns>Dictionary with variable name as key and list of points as value.</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetAllPointsBatch(string variable, DateTime? since)
    {
        var result = new Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>();
        var points = new List<(ulong characterId, DateTime timestamp, long value)>();
        var foundAny = false;

        foreach (var kvp in _cache)
        {
            if (kvp.Key.Variable != variable) continue;
            foundAny = true;

            var characterId = kvp.Key.CharacterId;
            var cachedPoints = since.HasValue 
                ? kvp.Value.GetPointsSince(since.Value)
                : kvp.Value.GetPoints();
            foreach (var (ts, val) in cachedPoints)
            {
                points.Add((characterId, ts, val));
            }
        }

        if (points.Count > 0)
        {
            result[variable] = points;
        }

        if (foundAny)
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose($"[Cache HIT] GetAllPointsBatch({variable}) - {points.Count} points");
        }
        else
        {
            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose($"[Cache MISS] GetAllPointsBatch({variable})");
        }

        return result;
    }

    /// <summary>
    /// Gets all cached points for variables matching a prefix+suffix pattern.
    /// Cache-first: returns only cached data, no DB fallback.
    /// Compatible with DbService.GetPointsBatchWithSuffix signature.
    /// </summary>
    /// <param name="prefix">Variable name prefix (e.g., "ItemRetainerX_").</param>
    /// <param name="suffix">Variable name suffix (e.g., "_12345").</param>
    /// <param name="since">Only return points after this timestamp. If null, returns all points.</param>
    /// <returns>Dictionary with variable name as key and list of points as value.</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPointsBatchWithSuffix(string prefix, string suffix, DateTime? since)
    {
        var result = new Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>();
        var foundAny = false;

        foreach (var kvp in _cache)
        {
            var variable = kvp.Key.Variable;
            if (!variable.StartsWith(prefix) || !variable.EndsWith(suffix)) continue;
            foundAny = true;

            if (!result.TryGetValue(variable, out var points))
            {
                points = new List<(ulong characterId, DateTime timestamp, long value)>();
                result[variable] = points;
            }

            var characterId = kvp.Key.CharacterId;
            var cachedPoints = since.HasValue 
                ? kvp.Value.GetPointsSince(since.Value)
                : kvp.Value.GetPoints();
            foreach (var (ts, val) in cachedPoints)
            {
                points.Add((characterId, ts, val));
            }
        }

        if (foundAny)
        {
            Interlocked.Increment(ref _cacheHits);
            LogService.Verbose($"[Cache HIT] GetPointsBatchWithSuffix({prefix}*{suffix}) - {result.Count} variables");
        }
        else
        {
            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose($"[Cache MISS] GetPointsBatchWithSuffix({prefix}*{suffix})");
        }

        return result;
    }

    /// <summary>
    /// Gets all cached variables that match a prefix.
    /// </summary>
    public IReadOnlyList<string> GetVariablesWithPrefix(string prefix)
    {
        return _cache.Keys
            .Where(k => k.Variable.StartsWith(prefix))
            .Select(k => k.Variable)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Checks if the cache has data for a variable (any character).
    /// </summary>
    public bool HasDataForVariable(string variable)
    {
        return _cache.Keys.Any(k => k.Variable == variable);
    }

    #endregion

    #endregion

    #region Cache Write Operations

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

        // Check if value is different from last point
        var lastPoint = cache.GetLastPoint();
        if (lastPoint.HasValue && lastPoint.Value.value == value)
        {
            return false; // No change, don't add duplicate
        }

        cache.AddPoint(ts, value);

        // Track available characters
        lock (_availableCharactersLock)
        {
            if (!_availableCharacters.TryGetValue(variable, out var chars))
            {
                chars = new HashSet<ulong>();
                _availableCharacters[variable] = chars;
            }
            chars.Add(characterId);
        }

        // Notify subscribers
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

        // Track available characters
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

    /// <summary>
    /// Populates available characters from database.
    /// </summary>
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

    /// <summary>
    /// Caches a character's game name (automatically detected from the game).
    /// </summary>
    public void SetCharacterName(ulong characterId, string name)
    {
        _characterDataCache.SetCharacterName(characterId, name);
    }

    /// <summary>
    /// Caches a character's custom display name.
    /// </summary>
    public void SetCharacterDisplayName(ulong characterId, string? displayName)
    {
        _characterDataCache.SetCharacterDisplayName(characterId, displayName);
    }

    /// <summary>
    /// Caches a character's time series color.
    /// </summary>
    public void SetCharacterTimeSeriesColor(ulong characterId, uint? color)
    {
        _characterDataCache.SetCharacterTimeSeriesColor(characterId, color);
    }

    /// <summary>
    /// Populates character names from database (with game name, display name, and color).
    /// </summary>
    public void PopulateCharacterNames(IEnumerable<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> names)
    {
        _characterDataCache.PopulateCharacterData(names);
    }

    /// <summary>
    /// Populates character names from database (simple version, treats name as game name).
    /// </summary>
    public void PopulateCharacterNamesSimple(IEnumerable<(ulong characterId, string? name)> names)
    {
        _characterDataCache.PopulateCharacterNamesSimple(names);
    }

    /// <summary>
    /// Invalidates cache for a specific variable/character combination.
    /// </summary>
    public void Invalidate(string variable, ulong characterId)
    {
        var key = new CacheKey(variable, characterId);
        _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Invalidates all cache entries for a variable.
    /// </summary>
    public void InvalidateVariable(string variable)
    {
        var keysToRemove = _cache.Keys.Where(k => k.Variable == variable).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        lock (_availableCharactersLock)
        {
            _availableCharacters.TryRemove(variable, out _);
        }
    }

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    public void ClearAll()
    {
        _cache.Clear();
        lock (_availableCharactersLock)
        {
            _availableCharacters.Clear();
        }
        _cacheHits = 0;
        _cacheMisses = 0;
    }

    /// <summary>
    /// Removes data for a specific character from all variables.
    /// </summary>
    public void RemoveCharacter(ulong characterId)
    {
        var keysToRemove = _cache.Keys.Where(k => k.CharacterId == characterId).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        _characterDataCache.RemoveCharacter(characterId);

        lock (_availableCharactersLock)
        {
            foreach (var chars in _availableCharacters.Values)
            {
                chars.Remove(characterId);
            }
        }
    }

    #endregion

    #region Cache Maintenance

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

        // Remove empty caches
        var emptyKeys = _cache.Where(kvp => kvp.Value.PointCount == 0).Select(kvp => kvp.Key).ToList();
        foreach (var key in emptyKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Gets cache statistics for diagnostics.
    /// </summary>
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

    #endregion

    #region Inventory Value Cache

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
                LogService.Verbose($"[Cache HIT] InventoryValueHistory - {_inventoryValueCache.Count} records");
                return _inventoryValueCache;
            }

            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose("[Cache MISS] InventoryValueHistory");
            return null;
        }
    }

    /// <summary>
    /// Updates the inventory value history cache with fresh data.
    /// </summary>
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
        
        LogService.Debug($"[TimeSeriesCacheService] Inventory value cache populated with {recordCount} records");
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
                LogService.Verbose($"[Cache HIT] InventoryValueHistory - {_inventoryValueCache.Count} records");
                return _inventoryValueCache;
            }

            Interlocked.Increment(ref _cacheMisses);
            LogService.Verbose("[Cache MISS] InventoryValueHistory - cache empty");
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
    
    /// <summary>
    /// Checks if inventory value cache has data.
    /// </summary>
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

    #endregion

    public void Dispose()
    {
        ClearAll();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Cache key combining variable name and character ID.
    /// </summary>
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

            // Enforce max points limit
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

/// <summary>
/// Configuration for the time-series cache.
/// </summary>
public class TimeSeriesCacheConfig
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

    /// <summary>
    /// Whether to pre-populate cache from database on startup.
    /// </summary>
    public bool PrePopulateOnStartup { get; set; } = true;

    /// <summary>
    /// Hours of historical data to load from DB on startup.
    /// Only applies when PrePopulateOnStartup is true.
    /// Default: 24 hours.
    /// </summary>
    public int StartupLoadHours { get; set; } = 24;
}

/// <summary>
/// Cache statistics for diagnostics.
/// </summary>
public readonly struct CacheStatistics
{
    public int SeriesCount { get; init; }
    public long TotalPoints { get; init; }
    public int CharacterCount { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public double HitRate { get; init; }
}
