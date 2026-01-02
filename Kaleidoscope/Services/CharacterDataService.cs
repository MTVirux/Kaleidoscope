using Kaleidoscope.Interfaces;
using Kaleidoscope.Models;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Represents a character with metadata for display and grouping.
/// </summary>
public sealed record CharacterInfo(
    ulong Id,
    string Name,
    string? WorldName = null,
    string? DataCenterName = null,
    string? RegionName = null)
{
    /// <summary>
    /// Creates a display name in "Name @ World" format if world is available.
    /// </summary>
    public string GetDisplayName() => !string.IsNullOrEmpty(WorldName) 
        ? $"{Name} @ {WorldName}" 
        : Name;
}

/// <summary>
/// Centralized service for character data access, caching, and formatting.
/// Consolidates character loading logic from CharacterCombo, DataTool, and TopInventoryValueTool.
/// </summary>
public sealed class CharacterDataService : IDisposable, IService
{
    private readonly IPluginLog _log;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService _configService;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly FavoritesService _favoritesService;

    private List<CharacterInfo>? _cachedCharacters;
    private Dictionary<ulong, CharacterInfo>? _cachedCharacterDict;
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _needsRefresh = true;

    /// <summary>
    /// Cache duration before automatic refresh.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Event raised when character data is refreshed.
    /// </summary>
    public event Action? OnCharactersRefreshed;

    public CharacterDataService(
        IPluginLog log,
        CurrencyTrackerService currencyTrackerService,
        ConfigurationService configService,
        FavoritesService favoritesService,
        AutoRetainerIpcService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
    {
        _log = log;
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;
        _favoritesService = favoritesService;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;

        // Subscribe to events that should trigger a refresh
        _favoritesService.OnFavoritesChanged += MarkDirty;
        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded += MarkDirty;
        }

        LogService.Debug(LogCategory.Character, "[CharacterDataService] Initialized");
    }

    /// <summary>
    /// Marks the cache as dirty, forcing a refresh on next access.
    /// </summary>
    public void MarkDirty()
    {
        _needsRefresh = true;
    }

    /// <summary>
    /// Gets all characters with optional filtering and sorting.
    /// </summary>
    /// <param name="includeAllCharactersOption">If true, includes an "All Characters" entry with ID 0.</param>
    /// <param name="sortByFavorites">If true, favorites appear first in the list.</param>
    /// <returns>List of character info, cached for performance.</returns>
    public IReadOnlyList<CharacterInfo> GetCharacters(
        bool includeAllCharactersOption = false,
        bool sortByFavorites = true)
    {
        EnsureCacheValid();

        if (_cachedCharacters == null)
            return Array.Empty<CharacterInfo>();

        var result = new List<CharacterInfo>();

        if (includeAllCharactersOption)
        {
            result.Add(new CharacterInfo(0, "All Characters"));
        }

        var characters = _cachedCharacters;
        if (sortByFavorites)
        {
            var favorites = _favoritesService.FavoriteCharacters;
            characters = characters
                .OrderByDescending(c => favorites.Contains(c.Id))
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        result.AddRange(characters);
        return result;
    }

    /// <summary>
    /// Gets a dictionary of character ID to CharacterInfo for fast lookups.
    /// </summary>
    public IReadOnlyDictionary<ulong, CharacterInfo> GetCharacterDictionary()
    {
        EnsureCacheValid();
        return _cachedCharacterDict ?? new Dictionary<ulong, CharacterInfo>();
    }

    /// <summary>
    /// Gets character names as parallel arrays for combo boxes.
    /// </summary>
    /// <param name="includeAllCharactersOption">If true, includes "All Characters" at index 0.</param>
    /// <returns>Tuple of (names array, ids array).</returns>
    public (string[] Names, ulong[] Ids) GetCharacterArrays(bool includeAllCharactersOption = true)
    {
        var characters = GetCharacters(includeAllCharactersOption, sortByFavorites: false);
        
        var names = new string[characters.Count];
        var ids = new ulong[characters.Count];

        for (int i = 0; i < characters.Count; i++)
        {
            names[i] = characters[i].Name;
            ids[i] = characters[i].Id;
        }

        return (names, ids);
    }

    /// <summary>
    /// Gets a character by ID.
    /// </summary>
    public CharacterInfo? GetCharacter(ulong characterId)
    {
        var dict = GetCharacterDictionary();
        return dict.TryGetValue(characterId, out var info) ? info : null;
    }

    /// <summary>
    /// Gets formatted character name applying the configured name format.
    /// </summary>
    public string GetFormattedName(ulong characterId)
    {
        var character = GetCharacter(characterId);
        if (character == null)
            return $"Character {characterId}";

        var nameFormat = _configService.Config.CharacterNameFormat;
        return TimeSeriesCacheService.FormatName(character.Name, nameFormat) ?? character.Name;
    }

    /// <summary>
    /// Filters characters by a set of allowed IDs.
    /// </summary>
    public IEnumerable<CharacterInfo> FilterByIds(IEnumerable<ulong> allowedIds)
    {
        var allowedSet = allowedIds.ToHashSet();
        return GetCharacters().Where(c => allowedSet.Contains(c.Id));
    }

    /// <summary>
    /// Gets characters grouped by world.
    /// </summary>
    public ILookup<string, CharacterInfo> GetCharactersByWorld()
    {
        return GetCharacters().ToLookup(
            c => c.WorldName ?? "Unknown",
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets characters grouped by data center.
    /// </summary>
    public ILookup<string, CharacterInfo> GetCharactersByDataCenter()
    {
        return GetCharacters().ToLookup(
            c => c.DataCenterName ?? "Unknown",
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets characters grouped by region.
    /// </summary>
    public ILookup<string, CharacterInfo> GetCharactersByRegion()
    {
        return GetCharacters().ToLookup(
            c => c.RegionName ?? "Unknown",
            StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureCacheValid()
    {
        var now = DateTime.UtcNow;
        if (!_needsRefresh && _cachedCharacters != null && (now - _lastRefresh) < CacheDuration)
            return;

        RefreshCache();
    }

    private void RefreshCache()
    {
        try
        {
            var characterDataCache = _currencyTrackerService.CharacterDataCache;
            var cacheService = _currencyTrackerService.CacheService;
            var worldData = _priceTrackingService?.WorldData;
            var nameFormat = _configService.Config.CharacterNameFormat;

            // Get raw character data from CharacterDataCache (no DB access)
            var dbCharacters = characterDataCache.GetAllCharacterNames()
                .Select(c => (c.characterId, c.name))
                .DistinctBy(c => c.characterId)
                .Where(c => c.characterId != 0)
                .ToList();

            // Get character world info from AutoRetainer
            var characterWorlds = GetCharacterWorlds();

            // Get disambiguated names from cache service
            var disambiguatedNames = characterDataCache.GetDisambiguatedNames(
                dbCharacters.Select(c => c.characterId));

            var characters = new List<CharacterInfo>();
            var characterDict = new Dictionary<ulong, CharacterInfo>();

            foreach (var (charId, rawName) in dbCharacters)
            {
                // Get formatted name
                var baseName = disambiguatedNames.TryGetValue(charId, out var formatted)
                    ? formatted
                    : cacheService.GetFormattedCharacterName(charId) ?? rawName ?? $"Character {charId}";

                // Extract world from name if present (format: "Name @ World")
                string? worldName = null;
                var displayName = baseName;
                var atIndex = baseName.IndexOf('@');
                if (atIndex > 0)
                {
                    worldName = baseName[(atIndex + 1)..].Trim();
                    displayName = baseName[..atIndex].Trim();
                }

                // Try to get world from AutoRetainer if not in name
                if (string.IsNullOrEmpty(worldName) && characterWorlds.TryGetValue(charId, out var arWorld))
                {
                    worldName = arWorld;
                }

                // Apply name format
                displayName = TimeSeriesCacheService.FormatName(displayName, nameFormat) ?? displayName;

                // Get DC and Region from world data
                string? dcName = null;
                string? regionName = null;
                if (!string.IsNullOrEmpty(worldName) && worldData != null)
                {
                    dcName = worldData.GetDataCenterForWorld(worldName)?.Name;
                    regionName = worldData.GetRegionForWorld(worldName);
                }

                var info = new CharacterInfo(charId, displayName, worldName, dcName, regionName);
                characters.Add(info);
                characterDict[charId] = info;
            }

            // Sort alphabetically by default
            characters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            _cachedCharacters = characters;
            _cachedCharacterDict = characterDict;
            _lastRefresh = DateTime.UtcNow;
            _needsRefresh = false;

            LogService.Debug(LogCategory.Character, $"[CharacterDataService] Refreshed cache with {characters.Count} characters");

            try
            {
                OnCharactersRefreshed?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.Warning(LogCategory.Character, $"[CharacterDataService] Error invoking OnCharactersRefreshed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Character, $"[CharacterDataService] Error refreshing cache: {ex.Message}");
            _cachedCharacters ??= new List<CharacterInfo>();
            _cachedCharacterDict ??= new Dictionary<ulong, CharacterInfo>();
        }
    }

    private Dictionary<ulong, string> GetCharacterWorlds()
    {
        var result = new Dictionary<ulong, string>();

        if (_autoRetainerService == null || !_autoRetainerService.IsAvailable)
            return result;

        try
        {
            var arData = _autoRetainerService.GetAllCharacterData();
            foreach (var (_, world, _, cid) in arData)
            {
                if (!string.IsNullOrEmpty(world))
                {
                    result[cid] = world;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Character, $"[CharacterDataService] Error getting AutoRetainer worlds: {ex.Message}");
        }

        return result;
    }

    public void Dispose()
    {
        _favoritesService.OnFavoritesChanged -= MarkDirty;
        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded -= MarkDirty;
        }
    }
}
