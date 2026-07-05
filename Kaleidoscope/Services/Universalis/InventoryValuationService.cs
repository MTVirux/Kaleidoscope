using Kaleidoscope.Models.Universalis;
using OtterGui.Services;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Database;
using Kaleidoscope.Services.Inventory;

namespace Kaleidoscope.Services.Universalis;

/// <summary>
/// Values character/retainer inventories from cached sale prices, writes periodic and event-driven
/// value snapshots, and computes top-items-by-value listings. Resolves the effective price-match
/// mode (World/DC/Region/Global) for a character's world. Collaborator of
/// <see cref="PriceTrackingService"/>, which orchestrates when snapshots are taken.
/// </summary>
public sealed class InventoryValuationService : IService, IDisposable
{
    private readonly ConfigurationService _configService;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly SalePriceCacheService _salePriceCacheService;
    private readonly TimeSeriesCacheService _cacheService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly CharacterDataCacheService _characterDataCache;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly WorldDataProvider _worldDataProvider;

    private volatile bool _disposed;

    private InventoryValueSettings ValueSettings => _configService.Config.InventoryValue;

    public InventoryValuationService(
        ConfigurationService configService,
        InventoryCacheService inventoryCacheService,
        SalePriceCacheService salePriceCacheService,
        TimeSeriesCacheService cacheService,
        KaleidoscopeDbService dbService,
        CharacterDataCacheService characterDataCache,
        CurrencyTrackerService currencyTrackerService,
        WorldDataProvider worldDataProvider)
    {
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _salePriceCacheService = salePriceCacheService;
        _cacheService = cacheService;
        _dbService = dbService;
        _characterDataCache = characterDataCache;
        _currencyTrackerService = currencyTrackerService;
        _worldDataProvider = worldDataProvider;
    }

    /// <summary>
    /// Gets the effective price match mode for a given world, considering the hierarchy:
    /// World override > DC override > Region override > Default.
    /// </summary>
    /// <param name="worldId">The world ID to get the price match mode for.</param>
    /// <returns>The effective price match mode.</returns>
    public PriceMatchMode GetEffectivePriceMatchMode(int worldId)
    {
        var settings = ValueSettings;

        if (settings.WorldPriceMatchModes.TryGetValue(worldId, out var worldMode))
            return worldMode;

        var worldData = _worldDataProvider.WorldData;
        if (worldData != null)
        {
            var dc = worldData.GetDataCenterForWorldId(worldId);
            if (dc?.Name != null && settings.DataCenterPriceMatchModes.TryGetValue(dc.Name, out var dcMode))
                return dcMode;

            if (dc?.Region != null && settings.RegionPriceMatchModes.TryGetValue(dc.Region, out var regionMode))
                return regionMode;
        }

        return settings.DefaultPriceMatchMode;
    }

    /// <summary>
    /// Gets the set of world IDs to include in inventory value calculations for a specific character's world.
    /// Returns null if all worlds should be included (Global mode).
    /// </summary>
    /// <param name="characterWorldId">The world ID of the character whose inventory is being valued.</param>
    /// <returns>Set of world IDs to include, or null for global (all worlds).</returns>
    private HashSet<int>? GetValueCalculationWorldIds(int characterWorldId)
    {
        var worldData = _worldDataProvider.WorldData;
        if (worldData == null) return null;

        var mode = GetEffectivePriceMatchMode(characterWorldId);
        return worldData.GetWorldIdsForPriceMatchMode(characterWorldId, mode);
    }

    /// <summary>
    /// Calculates the liquid value of a character's inventory.
    /// Uses in-memory cache for efficiency - offline characters' data is static.
    /// The price match mode is determined by the character's world.
    /// </summary>
    /// <returns>Tuple of (TotalValue, GilValue, ItemValue, ItemContributions).</returns>
    public async Task<(long TotalValue, long GilValue, long ItemValue, List<(int ItemId, long Quantity, int UnitPrice)> ItemContributions)> CalculateInventoryValueAsync(ulong characterId, bool includeRetainers = true)
    {
        var caches = _inventoryCacheService.GetInventoriesForCharacter(characterId);
        if (caches.Count == 0)
        {
            return (0, 0, 0, new List<(int, long, int)>());
        }

        var characterWorldId = GetCharacterWorldId(caches);

        long gilValue = 0;
        long itemValue = 0;
        var itemContributions = new List<(int ItemId, long Quantity, int UnitPrice)>();

        var itemQuantities = new Dictionary<int, long>();

        var marketableItems = _worldDataProvider.MarketableItems;

        foreach (var cache in caches)
        {
            if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                continue;

            gilValue += cache.Gil;

            foreach (var item in cache.Items)
            {
                if (marketableItems != null && !marketableItems.Contains((int)item.ItemId))
                    continue;

                // Skip bound items — they cannot be sold on the market board
                if (item.IsBound)
                    continue;

                if (!itemQuantities.ContainsKey((int)item.ItemId))
                    itemQuantities[(int)item.ItemId] = 0;

                itemQuantities[(int)item.ItemId] += item.Quantity;
            }
        }

        // Get prices for all items using filtered sale records based on character's world
        if (itemQuantities.Count > 0)
        {
            // Get included worlds based on the character's world and price match mode
            var includedWorldIds = characterWorldId.HasValue
                ? GetValueCalculationWorldIds(characterWorldId.Value)
                : null;

            var prices = _salePriceCacheService.GetLatestSalePrices(itemQuantities.Keys, includedWorldIds);

            foreach (var (itemId, quantity) in itemQuantities)
            {
                if (prices.TryGetValue(itemId, out var price))
                {
                    // Use last sale NQ price first, then HQ if no NQ
                    var unitPrice = price.LastSaleNq > 0 ? price.LastSaleNq : price.LastSaleHq;
                    itemValue += unitPrice * quantity;

                    itemContributions.Add((itemId, quantity, unitPrice));
                }
            }
        }

        return (gilValue + itemValue, gilValue, itemValue, itemContributions);
    }

    /// <summary>
    /// Gets the world ID for a character from their inventory cache entries.
    /// </summary>
    private int? GetCharacterWorldId(List<Models.Inventory.InventoryCacheEntry> caches)
    {
        var worldData = _worldDataProvider.WorldData;
        if (worldData == null) return null;

        // Find the player cache entry (not retainer) to get the world
        var playerCache = caches.FirstOrDefault(c => c.SourceType == Models.Inventory.InventorySourceType.Player);
        if (playerCache?.World == null) return null;

        return worldData.GetWorldId(playerCache.World);
    }

    /// <summary>
    /// Takes value snapshots for all known characters.
    /// Uses parallel processing to distribute CPU load across cores.
    /// Also queues samples to the standard time-series tracking.
    /// </summary>
    public async Task TakeValueSnapshotsAsync()
    {
        if (_disposed) return;

        try
        {
            var characterData = _characterDataCache.GetAllCharacterNames();
            var characterIds = characterData
                .Select(c => c.characterId)
                .Distinct()
                .ToList();

            if (characterIds.Count == 0) return;

            var characterNames = characterData.ToDictionary(c => c.characterId, c => c.name);

            var includeRetainers = _configService.Config.InventoryValue.IncludeRetainers;

            var tasks = characterIds.Select(async charId =>
            {
                var (total, gil, item, contributions) = await CalculateInventoryValueAsync(charId, includeRetainers);
                characterNames.TryGetValue(charId, out var name);
                return (charId, total, gil, item, contributions, name);
            }).ToList();

            var results = await Task.WhenAll(tasks);

            // Save results to database (must be sequential due to SQLite single-writer)
            foreach (var (charId, total, gil, item, contributions, characterName) in results)
            {
                _dbService.SaveInventoryValueHistory(charId, total, gil, item);

                // Also queue to standard time-series tracking
                // Only item value - Gil is tracked via Gil currency, Total can be merged in UI
                _currencyTrackerService.QueueInventoryValueSample(charId, item, characterName);
            }

            // Re-populate the full cache on background thread so main thread doesn't block
            PopulateInventoryValueCache();

            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Saved value snapshots for {characterIds.Count} characters (parallel)");
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Error taking value snapshots: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes value snapshots triggered by price updates or inventory changes.
    /// Similar to TakeValueSnapshotsAsync but only writes to time-series tables (not inventory_value_history)
    /// to avoid duplicating data. The inventory_value_history is still updated on the 15-minute interval.
    /// </summary>
    public async Task TakeEventDrivenValueSnapshotsAsync()
    {
        if (_disposed) return;

        try
        {
            var characterData = _characterDataCache.GetAllCharacterNames();
            var characterIds = characterData
                .Select(c => c.characterId)
                .Distinct()
                .ToList();

            if (characterIds.Count == 0) return;

            var characterNames = characterData.ToDictionary(c => c.characterId, c => c.name);

            var includeRetainers = _configService.Config.InventoryValue.IncludeRetainers;

            var tasks = characterIds.Select(async charId =>
            {
                var (total, gil, item, _) = await CalculateInventoryValueAsync(charId, includeRetainers);
                characterNames.TryGetValue(charId, out var name);
                return (charId, total, gil, item, name);
            }).ToList();

            var results = await Task.WhenAll(tasks);

            // Queue to standard time-series tracking (frequent updates)
            // Note: We don't write to inventory_value_history here - that's still on 15-minute interval
            // Only item value - Gil is tracked via Gil currency, Total can be merged in UI
            foreach (var (charId, total, gil, item, characterName) in results)
            {
                _currencyTrackerService.QueueInventoryValueSample(charId, item, characterName);
            }

            LogService.Verbose(LogCategory.PriceTracking, $"[PriceTracking] Event-driven value samples for {characterIds.Count} characters");
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Error taking event-driven value snapshots: {ex.Message}");
        }
    }

    /// <summary>
    /// Populates the in-memory inventory value cache from the database.
    /// This runs on the background thread so the main thread never hits the DB.
    /// </summary>
    public void PopulateInventoryValueCache()
    {
        try
        {
            var historyData = _dbService.GetAllInventoryValueHistory();
            _cacheService.SetInventoryValueCache(historyData);
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Populated inventory value cache with {historyData.Count} records");
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Error populating inventory value cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the top items by value for a character or all characters.
    /// When a specific character is specified, uses their world's price match mode.
    /// When all characters are requested, uses global prices.
    /// </summary>
    public async Task<List<(int ItemId, long Quantity, long Value)>> GetTopItemsByValueAsync(
        ulong? characterId = null,
        int maxItems = 100,
        bool includeRetainers = true)
    {
        var result = new List<(int, long, long)>();

        try
        {
            List<Models.Inventory.InventoryCacheEntry> caches;
            int? characterWorldId = null;

            if (characterId.HasValue)
            {
                caches = _inventoryCacheService.GetInventoriesForCharacter(characterId.Value);
                characterWorldId = GetCharacterWorldId(caches);
            }
            else
            {
                caches = _inventoryCacheService.GetAllInventories().ToList();
            }

            // Aggregate item quantities
            var itemQuantities = new Dictionary<int, long>();

            var marketableItems = _worldDataProvider.MarketableItems;

            foreach (var cache in caches)
            {
                if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                    continue;

                foreach (var item in cache.Items)
                {
                    if (marketableItems != null && !marketableItems.Contains((int)item.ItemId))
                        continue;

                    // Skip bound items — they cannot be sold on the market board
                    if (item.IsBound)
                        continue;

                    if (!itemQuantities.ContainsKey((int)item.ItemId))
                        itemQuantities[(int)item.ItemId] = 0;

                    itemQuantities[(int)item.ItemId] += item.Quantity;
                }
            }

            // Get prices using filtered sale records based on character's world (or global for all)
            var includedWorldIds = characterWorldId.HasValue
                ? GetValueCalculationWorldIds(characterWorldId.Value)
                : null; // Use global prices for multi-character view
            var prices = _salePriceCacheService.GetLatestSalePrices(itemQuantities.Keys, includedWorldIds);

            // Calculate values using last sale prices
            foreach (var (itemId, quantity) in itemQuantities)
            {
                if (prices.TryGetValue(itemId, out var price))
                {
                    var unitPrice = price.LastSaleNq > 0 ? price.LastSaleNq : price.LastSaleHq;
                    var value = unitPrice * quantity;
                    result.Add((itemId, quantity, value));
                }
            }

            // Sort by value descending and take top N
            result = result
                .OrderByDescending(x => x.Item3)
                .Take(maxItems)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Error getting top items: {ex.Message}");
        }

        return result;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
