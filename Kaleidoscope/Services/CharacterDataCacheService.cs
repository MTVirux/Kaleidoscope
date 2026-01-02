using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Centralized cache service for all character-related data (names, display names, colors, metadata).
/// Provides instant read access while managing write-through persistence to the database.
/// </summary>
/// <remarks>
/// Key design principles:
/// 1. All reads come from memory cache (no DB queries after initialization)
/// 2. Writes update cache immediately, then persist to DB
/// 3. Cache is populated from DB on startup
/// 4. Single source of truth for character metadata across all UI components
/// </remarks>
public sealed class CharacterDataCacheService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;

    // Main cache: characterId -> character data
    private readonly ConcurrentDictionary<ulong, CharacterCacheEntry> _cache = new();

    // DB service reference for persistence (set after construction due to circular dependency)
    private KaleidoscopeDbService? _dbService;
    private volatile bool _initialized;

    // Cache statistics for monitoring
    private long _cacheHits;
    private long _cacheMisses;

    /// <summary>Event fired when character data is updated.</summary>
    public event Action<ulong>? OnCharacterUpdated;

    /// <summary>Gets cache hit count for diagnostics.</summary>
    public long CacheHits => _cacheHits;

    /// <summary>Gets cache miss count for diagnostics.</summary>
    public long CacheMisses => _cacheMisses;

    /// <summary>Gets total number of cached characters.</summary>
    public int CachedCharacterCount => _cache.Count;

    /// <summary>Gets whether the cache has been initialized from DB.</summary>
    public bool IsInitialized => _initialized;

    public CharacterDataCacheService(IPluginLog log, ConfigurationService configService)
    {
        _log = log;
        _configService = configService;
        LogService.Debug(LogCategory.Character, "[CharacterDataCacheService] Initialized (awaiting DB connection)");
    }

    #region Initialization

    /// <summary>
    /// Sets the database service reference and populates the cache.
    /// Called by CurrencyTrackerService after DbService is created.
    /// </summary>
    public void Initialize(KaleidoscopeDbService dbService)
    {
        if (_initialized) return;

        _dbService = dbService;
        PopulateFromDatabase();
        _initialized = true;
        LogService.Debug(LogCategory.Character, $"[CharacterDataCacheService] Initialized with {_cache.Count} characters from database");
    }

    /// <summary>
    /// Populates the cache from the database.
    /// </summary>
    private void PopulateFromDatabase()
    {
        if (_dbService == null) return;

        try
        {
            var allData = _dbService.GetAllCharacterDataExtended();
            foreach (var (characterId, gameName, displayName, timeSeriesColor) in allData)
            {
                _cache[characterId] = new CharacterCacheEntry
                {
                    CharacterId = characterId,
                    GameName = gameName,
                    DisplayName = displayName,
                    TimeSeriesColor = timeSeriesColor
                };
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Character, $"[CharacterDataCacheService] Failed to populate from database: {ex.Message}");
        }
    }

    /// <summary>
    /// Forces a refresh of the cache from the database.
    /// Use sparingly - normally cache should be self-maintaining.
    /// </summary>
    public void RefreshFromDatabase()
    {
        _cache.Clear();
        PopulateFromDatabase();
        LogService.Debug(LogCategory.Character, $"[CharacterDataCacheService] Refreshed cache with {_cache.Count} characters");
    }

    #endregion

    #region Read Operations

    /// <summary>
    /// Gets the effective display name for a character (custom display name if set, otherwise game name).
    /// </summary>
    public string? GetCharacterName(ulong characterId)
    {
        if (_cache.TryGetValue(characterId, out var entry))
        {
            Interlocked.Increment(ref _cacheHits);
            return entry.DisplayName ?? entry.GameName;
        }

        Interlocked.Increment(ref _cacheMisses);
        return null;
    }

    /// <summary>
    /// Gets the game name for a character (automatically detected from the game).
    /// </summary>
    public string? GetCharacterGameName(ulong characterId)
    {
        if (_cache.TryGetValue(characterId, out var entry))
        {
            Interlocked.Increment(ref _cacheHits);
            return entry.GameName;
        }

        Interlocked.Increment(ref _cacheMisses);
        return null;
    }

    /// <summary>
    /// Gets the custom display name for a character (null if not set).
    /// </summary>
    public string? GetCharacterDisplayName(ulong characterId)
    {
        if (_cache.TryGetValue(characterId, out var entry))
        {
            Interlocked.Increment(ref _cacheHits);
            return entry.DisplayName;
        }

        Interlocked.Increment(ref _cacheMisses);
        return null;
    }

    /// <summary>
    /// Gets the time series color for a character (null if not set).
    /// </summary>
    public uint? GetCharacterTimeSeriesColor(ulong characterId)
    {
        if (_cache.TryGetValue(characterId, out var entry))
        {
            Interlocked.Increment(ref _cacheHits);
            return entry.TimeSeriesColor;
        }

        Interlocked.Increment(ref _cacheMisses);
        return null;
    }

    /// <summary>
    /// Gets both game name and display name for a character.
    /// </summary>
    public (string? GameName, string? DisplayName) GetCharacterNames(ulong characterId)
    {
        if (_cache.TryGetValue(characterId, out var entry))
        {
            Interlocked.Increment(ref _cacheHits);
            return (entry.GameName, entry.DisplayName);
        }

        Interlocked.Increment(ref _cacheMisses);
        return (null, null);
    }

    /// <summary>
    /// Gets all character data (game name, display name, and time series color).
    /// </summary>
    public (string? GameName, string? DisplayName, uint? TimeSeriesColor) GetCharacterData(ulong characterId)
    {
        if (_cache.TryGetValue(characterId, out var entry))
        {
            Interlocked.Increment(ref _cacheHits);
            return (entry.GameName, entry.DisplayName, entry.TimeSeriesColor);
        }

        Interlocked.Increment(ref _cacheMisses);
        return (null, null, null);
    }

    /// <summary>
    /// Checks if a character exists in the cache.
    /// </summary>
    public bool HasCharacter(ulong characterId)
    {
        return _cache.ContainsKey(characterId);
    }

    /// <summary>
    /// Gets all cached character IDs.
    /// </summary>
    public IReadOnlyList<ulong> GetAllCharacterIds()
    {
        return _cache.Keys.ToList();
    }

    /// <summary>
    /// Gets all stored character name mappings (display_name if set, otherwise game name).
    /// </summary>
    public List<(ulong characterId, string? name)> GetAllCharacterNames()
    {
        var result = new List<(ulong, string?)>(_cache.Count);
        foreach (var kvp in _cache)
        {
            result.Add((kvp.Key, kvp.Value.DisplayName ?? kvp.Value.GameName));
        }
        return result;
    }

    /// <summary>
    /// Gets all stored character name mappings with both game and display names.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName)> GetAllCharacterNamesExtended()
    {
        var result = new List<(ulong, string?, string?)>(_cache.Count);
        foreach (var kvp in _cache)
        {
            result.Add((kvp.Key, kvp.Value.GameName, kvp.Value.DisplayName));
        }
        return result;
    }

    /// <summary>
    /// Gets all stored character data including time series colors.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> GetAllCharacterDataExtended()
    {
        var result = new List<(ulong, string?, string?, uint?)>(_cache.Count);
        foreach (var kvp in _cache)
        {
            result.Add((kvp.Key, kvp.Value.GameName, kvp.Value.DisplayName, kvp.Value.TimeSeriesColor));
        }
        return result;
    }

    /// <summary>
    /// Gets all stored character name mappings as a dictionary (display_name if set, otherwise game name).
    /// </summary>
    public IReadOnlyDictionary<ulong, string?> GetAllCharacterNamesDict()
    {
        var result = new Dictionary<ulong, string?>(_cache.Count);
        foreach (var kvp in _cache)
        {
            result[kvp.Key] = kvp.Value.DisplayName ?? kvp.Value.GameName;
        }
        return result;
    }

    /// <summary>
    /// Gets a formatted character name based on the current name format setting.
    /// Returns display_name if set (unformatted), otherwise formats the game name.
    /// </summary>
    public string? GetFormattedCharacterName(ulong characterId)
    {
        if (!_cache.TryGetValue(characterId, out var entry))
            return null;

        // If a custom display name is set, use it as-is (user's choice)
        if (!string.IsNullOrEmpty(entry.DisplayName))
            return entry.DisplayName;

        // Format the game name according to setting
        return FormatName(entry.GameName, _configService.Config.CharacterNameFormat);
    }

    /// <summary>
    /// Gets disambiguated display names for a set of character IDs.
    /// When multiple characters have the same formatted name, appends a short identifier.
    /// </summary>
    public Dictionary<ulong, string> GetDisambiguatedNames(IEnumerable<ulong> characterIds)
    {
        var idList = characterIds.ToList();
        var result = new Dictionary<ulong, string>();

        // First pass: get all base names
        var baseNames = new Dictionary<ulong, string>();
        foreach (var cid in idList)
        {
            var name = GetFormattedCharacterName(cid) ?? $"...{cid % 1_000_000:D6}";
            baseNames[cid] = name;
        }

        // Detect collisions
        var nameCounts = baseNames.Values.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        // Second pass: disambiguate where needed
        foreach (var (cid, baseName) in baseNames)
        {
            if (nameCounts.Contains(baseName))
            {
                result[cid] = $"{baseName} (#{cid % 10000:D4})";
            }
            else
            {
                result[cid] = baseName;
            }
        }

        return result;
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

    #endregion

    #region Write Operations

    /// <summary>
    /// Sets a character's game name (automatically detected from the game).
    /// Updates cache immediately and persists to DB.
    /// </summary>
    public void SetCharacterName(ulong characterId, string name)
    {
        if (string.IsNullOrEmpty(name) || characterId == 0) return;

        var entry = _cache.GetOrAdd(characterId, _ => new CharacterCacheEntry { CharacterId = characterId });
        entry.GameName = name;

        // Persist to DB
        _dbService?.SaveCharacterName(characterId, name);

        OnCharacterUpdated?.Invoke(characterId);
    }

    /// <summary>
    /// Sets a character's custom display name.
    /// Updates cache immediately and persists to DB.
    /// </summary>
    public void SetCharacterDisplayName(ulong characterId, string? displayName)
    {
        if (characterId == 0) return;

        var entry = _cache.GetOrAdd(characterId, _ => new CharacterCacheEntry { CharacterId = characterId });
        entry.DisplayName = displayName;

        // Persist to DB
        _dbService?.SaveCharacterDisplayName(characterId, displayName);

        OnCharacterUpdated?.Invoke(characterId);
    }

    /// <summary>
    /// Sets a character's time series color.
    /// Updates cache immediately and persists to DB.
    /// </summary>
    public void SetCharacterTimeSeriesColor(ulong characterId, uint? color)
    {
        if (characterId == 0) return;

        var entry = _cache.GetOrAdd(characterId, _ => new CharacterCacheEntry { CharacterId = characterId });
        entry.TimeSeriesColor = color;

        // Persist to DB
        _dbService?.SaveCharacterTimeSeriesColor(characterId, color);

        OnCharacterUpdated?.Invoke(characterId);
    }

    /// <summary>
    /// Ensures a character exists in the cache with the given name.
    /// Used when discovering new characters from game data.
    /// </summary>
    public void EnsureCharacter(ulong characterId, string? gameName = null)
    {
        if (characterId == 0) return;

        if (!string.IsNullOrEmpty(gameName))
        {
            SetCharacterName(characterId, gameName);
        }
        else if (!_cache.ContainsKey(characterId))
        {
            _cache[characterId] = new CharacterCacheEntry { CharacterId = characterId };
        }
    }

    /// <summary>
    /// Removes a character from the cache.
    /// Note: This does NOT delete the character from the database.
    /// </summary>
    public void RemoveCharacter(ulong characterId)
    {
        _cache.TryRemove(characterId, out _);
    }

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    public void ClearAll()
    {
        _cache.Clear();
        _cacheHits = 0;
        _cacheMisses = 0;
    }

    #endregion

    #region Bulk Operations

    /// <summary>
    /// Populates the cache with character data from an external source.
    /// Used during initialization or when syncing from external data.
    /// </summary>
    public void PopulateCharacterData(IEnumerable<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> data)
    {
        foreach (var (cid, gameName, displayName, color) in data)
        {
            if (cid == 0) continue;

            var entry = _cache.GetOrAdd(cid, _ => new CharacterCacheEntry { CharacterId = cid });
            if (!string.IsNullOrEmpty(gameName))
                entry.GameName = gameName;
            if (!string.IsNullOrEmpty(displayName))
                entry.DisplayName = displayName;
            if (color.HasValue)
                entry.TimeSeriesColor = color;
        }
    }

    /// <summary>
    /// Populates character names from a simple name list.
    /// </summary>
    public void PopulateCharacterNamesSimple(IEnumerable<(ulong characterId, string? name)> names)
    {
        foreach (var (cid, name) in names)
        {
            if (cid == 0 || string.IsNullOrEmpty(name)) continue;

            var entry = _cache.GetOrAdd(cid, _ => new CharacterCacheEntry { CharacterId = cid });
            entry.GameName = name;
        }
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets cache statistics for diagnostics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            SeriesCount = 0,
            TotalPoints = 0,
            CharacterCount = _cache.Count,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            HitRate = _cacheHits + _cacheMisses > 0
                ? (double)_cacheHits / (_cacheHits + _cacheMisses)
                : 0.0
        };
    }

    #endregion

    public void Dispose()
    {
        _cache.Clear();
        LogService.Debug(LogCategory.Character, "[CharacterDataCacheService] Disposed");
    }
}

/// <summary>
/// Cache entry for a single character's data.
/// </summary>
public class CharacterCacheEntry
{
    public ulong CharacterId { get; set; }
    public string? GameName { get; set; }
    public string? DisplayName { get; set; }
    public uint? TimeSeriesColor { get; set; }
    public string? WorldName { get; set; }
    public string? DataCenterName { get; set; }
}
