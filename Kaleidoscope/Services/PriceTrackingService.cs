using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Universalis;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service for tracking item prices over time using Universalis API and WebSocket.
/// Manages price data persistence, retention policies, and inventory value calculations.
/// </summary>
public sealed class PriceTrackingService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly ConfigurationService _configService;
    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly SamplerService _samplerService;
    private readonly InventoryCacheService _inventoryCacheService;

    // Cached world/DC data from Universalis
    private UniversalisWorldData? _worldData;
    private HashSet<int>? _marketableItems;
    private DateTime _lastWorldDataFetch = DateTime.MinValue;
    private DateTime _lastMarketableItemsFetch = DateTime.MinValue;
    private DateTime _lastCleanup = DateTime.MinValue;
    private DateTime _lastValueSnapshot = DateTime.MinValue;

    // In-memory price cache for quick lookups
    private readonly ConcurrentDictionary<(int itemId, int worldId), (int minNq, int minHq, DateTime updated)> _priceCache = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disposed;

    private const int WorldDataRefreshHours = 24;
    private const int MarketableItemsRefreshHours = 24;
    private const int ValueSnapshotIntervalMinutes = 15;

    private PriceTrackingSettings Settings => _configService.Config.PriceTracking;
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    /// <summary>Gets the cached world data.</summary>
    public UniversalisWorldData? WorldData => _worldData;

    /// <summary>Gets the Universalis service for API calls.</summary>
    public UniversalisService UniversalisService => _universalisService;

    /// <summary>Gets the set of marketable item IDs.</summary>
    public IReadOnlySet<int>? MarketableItems => _marketableItems;

    /// <summary>Gets whether the service is initialized.</summary>
    public bool IsInitialized => _worldData != null && _marketableItems != null;

    /// <summary>Gets whether the WebSocket is currently connected for real-time price updates.</summary>
    public bool IsSocketConnected => _webSocketService.IsConnected;

    /// <summary>Event fired when price data is updated (new price received via WebSocket).</summary>
    public event Action<int>? OnPriceDataUpdated;

    public PriceTrackingService(
        IPluginLog log,
        IFramework framework,
        ConfigurationService configService,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        SamplerService samplerService,
        InventoryCacheService inventoryCacheService)
    {
        _log = log;
        _framework = framework;
        _configService = configService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _samplerService = samplerService;
        _inventoryCacheService = inventoryCacheService;

        // Subscribe to WebSocket events
        _webSocketService.OnPriceUpdate += OnPriceUpdate;

        // Subscribe to framework update for periodic tasks
        _framework.Update += OnFrameworkUpdate;

        _log.Debug("[PriceTracking] Service initialized");

        // Start async initialization
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _log.Debug("[PriceTracking] InitializeAsync starting");
            
            if (_disposed)
            {
                _log.Debug("[PriceTracking] InitializeAsync - already disposed, exiting");
                return;
            }
            
            // Fetch world/DC data and marketable items in parallel for faster startup
            var worldDataTask = RefreshWorldDataAsync();
            var marketableItemsTask = RefreshMarketableItemsAsync();
            
            await Task.WhenAll(worldDataTask, marketableItemsTask);

            if (_disposed)
            {
                _log.Debug("[PriceTracking] InitializeAsync - disposed after data fetch, exiting");
                return;
            }
            
            // Start WebSocket if enabled
            _log.Debug($"[PriceTracking] InitializeAsync - Settings.Enabled = {Settings.Enabled}");
            if (Settings.Enabled)
            {
                _log.Debug("[PriceTracking] InitializeAsync - starting WebSocket");
                await _webSocketService.StartAsync();
                await _webSocketService.SubscribeToAllAsync();
                
                // Fetch prices for stale inventory items at startup
                await FetchStaleInventoryPricesAsync();
            }

            _log.Debug("[PriceTracking] Initialization complete");
        }
        catch (Exception ex)
        {
            _log.Error($"[PriceTracking] Initialization failed: {ex.Message}");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_disposed)
            return;
        
        var now = DateTime.UtcNow;

        // Periodic cleanup
        if ((now - _lastCleanup).TotalMinutes >= Settings.CleanupIntervalMinutes)
        {
            _lastCleanup = now;
            _ = Task.Run(PerformCleanupAsync);
        }

        // Periodic value snapshots
        if ((now - _lastValueSnapshot).TotalMinutes >= ValueSnapshotIntervalMinutes && Settings.Enabled)
        {
            _lastValueSnapshot = now;
            _ = Task.Run(TakeValueSnapshotsAsync);
        }

        // Refresh world data periodically
        if ((now - _lastWorldDataFetch).TotalHours >= WorldDataRefreshHours)
        {
            _lastWorldDataFetch = now;
            _ = Task.Run(RefreshWorldDataAsync);
        }

        // Refresh marketable items periodically
        if ((now - _lastMarketableItemsFetch).TotalHours >= MarketableItemsRefreshHours)
        {
            _lastMarketableItemsFetch = now;
            _ = Task.Run(RefreshMarketableItemsAsync);
        }
    }

    private void OnPriceUpdate(PriceFeedEntry entry)
    {
        try
        {
            // Check if this item is excluded
            if (Settings.ExcludedItemIds.Contains(entry.ItemId))
                return;

            // Check if this world is in our scope
            if (!IsWorldInScope(entry.WorldId))
                return;

            var key = (entry.ItemId, entry.WorldId);

            // Check if this is a sale event or a listing event
            var isSale = entry.EventType == "Sale";

            if (isSale)
            {
                // Sale event - save individual sale record for per-world filtering
                DbService.SaveSaleRecord(
                    entry.ItemId, 
                    entry.WorldId, 
                    entry.PricePerUnit, 
                    entry.Quantity, 
                    entry.IsHq, 
                    entry.Total,
                    entry.BuyerName);

                // Also update the aggregated last sale prices for backward compatibility
                var lastSaleNq = entry.IsHq ? 0 : entry.PricePerUnit;
                var lastSaleHq = entry.IsHq ? entry.PricePerUnit : 0;

                // Get existing cached prices to preserve min prices
                var existingNq = 0;
                var existingHq = 0;
                if (_priceCache.TryGetValue(key, out var existing))
                {
                    existingNq = existing.minNq;
                    existingHq = existing.minHq;
                }

                // Save to database with last sale prices
                DbService.SaveItemPrice(entry.ItemId, entry.WorldId, existingNq, existingHq, 
                    lastSaleNq: lastSaleNq, lastSaleHq: lastSaleHq);

                // Notify listeners that price data has been updated
                OnPriceDataUpdated?.Invoke(entry.ItemId);
            }
            else
            {
                // Listing event - update min price cache
                var price = entry.IsHq 
                    ? (0, entry.PricePerUnit) 
                    : (entry.PricePerUnit, 0);

                if (_priceCache.TryGetValue(key, out var existing))
                {
                    // Merge with existing - keep lower prices
                    var newNq = price.Item1 > 0 ? 
                        (existing.minNq > 0 ? Math.Min(existing.minNq, price.Item1) : price.Item1) 
                        : existing.minNq;
                    var newHq = price.Item2 > 0 ? 
                        (existing.minHq > 0 ? Math.Min(existing.minHq, price.Item2) : price.Item2) 
                        : existing.minHq;
                    _priceCache[key] = (newNq, newHq, DateTime.UtcNow);
                }
                else
                {
                    _priceCache[key] = (price.Item1, price.Item2, DateTime.UtcNow);
                }

                // Save to database
                if (_priceCache.TryGetValue(key, out var cached))
                {
                    DbService.SaveItemPrice(entry.ItemId, entry.WorldId, cached.minNq, cached.minHq);

                    // Notify listeners that price data has been updated
                    OnPriceDataUpdated?.Invoke(entry.ItemId);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] Error processing price update: {ex.Message}");
        }
    }

    private bool IsWorldInScope(int worldId)
    {
        var settings = Settings;

        switch (settings.ScopeMode)
        {
            case PriceTrackingScopeMode.All:
                return true;

            case PriceTrackingScopeMode.ByWorld:
                return settings.SelectedWorldIds.Contains(worldId);

            case PriceTrackingScopeMode.ByDataCenter:
                if (_worldData == null) return true;
                var worldName = _worldData.GetWorldName(worldId);
                if (worldName == null) return true;
                foreach (var dcName in settings.SelectedDataCenters)
                {
                    var dc = _worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                    if (dc?.Worlds?.Contains(worldId) == true) return true;
                }
                return false;

            case PriceTrackingScopeMode.ByRegion:
                if (_worldData == null) return true;
                foreach (var regionName in settings.SelectedRegions)
                {
                    foreach (var dc in _worldData.GetDataCentersForRegion(regionName))
                    {
                        if (dc.Worlds?.Contains(worldId) == true) return true;
                    }
                }
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// Refreshes the cached world/DC data from Universalis.
    /// </summary>
    public async Task RefreshWorldDataAsync()
    {
        if (_disposed) return;
        
        try
        {
            _log.Debug("[PriceTracking] Fetching world data from Universalis");

            // Fetch worlds and data centers in parallel
            var worldsTask = _universalisService.GetWorldsAsync();
            var dataCentersTask = _universalisService.GetDataCentersAsync();
            
            await Task.WhenAll(worldsTask, dataCentersTask);
            
            var worlds = await worldsTask;
            var dataCenters = await dataCentersTask;

            if (worlds != null && dataCenters != null)
            {
                _worldData = new UniversalisWorldData
                {
                    Worlds = worlds,
                    DataCenters = dataCenters,
                    LastUpdated = DateTime.UtcNow
                };
                _lastWorldDataFetch = DateTime.UtcNow;

                _log.Debug($"[PriceTracking] Loaded {worlds.Count} worlds, {dataCenters.Count} data centers");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"[PriceTracking] Failed to fetch world data: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes the list of marketable items from Universalis.
    /// </summary>
    public async Task RefreshMarketableItemsAsync()
    {
        if (_disposed) return;
        
        try
        {
            _log.Debug("[PriceTracking] Fetching marketable items from Universalis");

            var items = await _universalisService.GetMarketableItemsAsync();

            if (items != null)
            {
                _marketableItems = items.ToHashSet();
                _lastMarketableItemsFetch = DateTime.UtcNow;

                _log.Debug($"[PriceTracking] Loaded {items.Count} marketable items");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"[PriceTracking] Failed to fetch marketable items: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current price for an item.
    /// First checks cache, then database, optionally fetches from API.
    /// </summary>
    public async Task<(int MinPriceNq, int MinPriceHq)?> GetItemPriceAsync(int itemId, int? worldId = null, bool fetchIfMissing = true)
    {
        // Check if item is marketable
        if (_marketableItems != null && !_marketableItems.Contains(itemId))
        {
            return null;
        }

        // Try memory cache first
        if (worldId.HasValue)
        {
            var key = (itemId, worldId.Value);
            if (_priceCache.TryGetValue(key, out var cached))
            {
                return (cached.minNq, cached.minHq);
            }
        }

        // Try database
        var dbResult = worldId.HasValue 
            ? DbService.GetItemPrice(itemId, worldId.Value)
            : null;
        
        if (dbResult.HasValue)
        {
            return (dbResult.Value.MinPriceNq, dbResult.Value.MinPriceHq);
        }

        // Optionally fetch from API
        if (fetchIfMissing)
        {
            return await FetchPriceFromApiAsync(itemId, worldId);
        }

        return null;
    }

    /// <summary>
    /// Fetches price from Universalis API and caches it.
    /// </summary>
    public async Task<(int MinPriceNq, int MinPriceHq)?> FetchPriceFromApiAsync(int itemId, int? worldId = null)
    {
        try
        {
            var scope = _universalisService.GetConfiguredScope();
            if (string.IsNullOrEmpty(scope))
            {
                return null;
            }

            _log.Debug($"[PriceTracking] Fetching price for item {itemId} from API");

            var data = await _universalisService.GetAggregatedDataAsync(scope, (uint)itemId);
            if (data?.Results == null || data.Results.Count == 0)
            {
                return null;
            }

            var result = data.Results[0];
            var nqPrice = result.Nq?.MinListing?.World?.Price ?? 0;
            var hqPrice = result.Hq?.MinListing?.World?.Price ?? 0;
            var lastSaleNq = result.Nq?.RecentPurchase?.World?.Price ?? 0;
            var lastSaleHq = result.Hq?.RecentPurchase?.World?.Price ?? 0;

            // Cache in memory
            if (worldId.HasValue)
            {
                _priceCache[(itemId, worldId.Value)] = (nqPrice, hqPrice, DateTime.UtcNow);
            }

            // Save to database - we need the world ID
            // For now, use the config scope's world if available
            if (_worldData != null)
            {
                var wid = _worldData.GetWorldId(scope);
                if (wid.HasValue)
                {
                    DbService.SaveItemPrice(itemId, wid.Value, nqPrice, hqPrice, 
                        lastSaleNq: lastSaleNq, lastSaleHq: lastSaleHq);
                }
            }

            return (nqPrice, hqPrice);
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] API fetch failed for item {itemId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Calculates the liquid value of a character's inventory.
    /// Uses in-memory cache for efficiency - offline characters' data is static.
    /// </summary>
    /// <returns>Tuple of (TotalValue, GilValue, ItemValue, ItemContributions).</returns>
    public async Task<(long TotalValue, long GilValue, long ItemValue, List<(int ItemId, long Quantity, int UnitPrice)> ItemContributions)> CalculateInventoryValueAsync(ulong characterId, bool includeRetainers = true)
    {
        var caches = _inventoryCacheService.GetInventoriesForCharacter(characterId);
        if (caches.Count == 0)
        {
            return (0, 0, 0, new List<(int, long, int)>());
        }

        long gilValue = 0;
        long itemValue = 0;
        var itemContributions = new List<(int ItemId, long Quantity, int UnitPrice)>();

        // Collect all unique item IDs
        var itemQuantities = new Dictionary<int, long>();

        foreach (var cache in caches)
        {
            // Skip retainers if not included
            if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                continue;

            gilValue += cache.Gil;

            foreach (var item in cache.Items)
            {
                if (_marketableItems != null && !_marketableItems.Contains((int)item.ItemId))
                    continue;

                if (!itemQuantities.ContainsKey((int)item.ItemId))
                    itemQuantities[(int)item.ItemId] = 0;
                
                itemQuantities[(int)item.ItemId] += item.Quantity;
            }
        }

        // Get prices for all items using new filtered sale records
        if (itemQuantities.Count > 0)
        {
            // Get excluded worlds from settings
            var excludedWorldIds = Settings.ExcludedWorldIds.Count > 0 
                ? Settings.ExcludedWorldIds 
                : null;

            var prices = DbService.GetLatestSalePrices(itemQuantities.Keys, excludedWorldIds);

            foreach (var (itemId, quantity) in itemQuantities)
            {
                if (prices.TryGetValue(itemId, out var price))
                {
                    // Use last sale NQ price first, then HQ if no NQ
                    var unitPrice = price.LastSaleNq > 0 ? price.LastSaleNq : price.LastSaleHq;
                    itemValue += unitPrice * quantity;
                    
                    // Record the contribution for historical tracking
                    itemContributions.Add((itemId, quantity, unitPrice));
                }
            }
        }

        return (gilValue + itemValue, gilValue, itemValue, itemContributions);
    }

    /// <summary>
    /// Takes value snapshots for all known characters.
    /// Uses parallel processing to distribute CPU load across cores.
    /// </summary>
    private async Task TakeValueSnapshotsAsync()
    {
        if (_disposed) return;
        
        try
        {
            var characterIds = DbService.GetAllCharacterNames()
                .Select(c => c.characterId)
                .Distinct()
                .ToList();

            if (characterIds.Count == 0) return;

            var includeRetainers = _configService.Config.InventoryValue.IncludeRetainers;
            
            // Calculate values for all characters in parallel
            var tasks = characterIds.Select(async charId =>
            {
                var (total, gil, item, contributions) = await CalculateInventoryValueAsync(charId, includeRetainers);
                return (charId, total, gil, item, contributions);
            }).ToList();

            var results = await Task.WhenAll(tasks);

            // Save results to database (must be sequential due to SQLite single-writer)
            foreach (var (charId, total, gil, item, contributions) in results)
            {
                DbService.SaveInventoryValueHistory(charId, total, gil, item, contributions);
            }

            _log.Debug($"[PriceTracking] Saved value snapshots for {characterIds.Count} characters (parallel)");
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] Error taking value snapshots: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs cleanup of old price data based on retention settings.
    /// </summary>
    private async Task PerformCleanupAsync()
    {
        if (_disposed) return;
        
        try
        {
            var settings = Settings;

            switch (settings.RetentionType)
            {
                case PriceRetentionType.ByTime:
                    var deleted = DbService.CleanupOldPriceData(settings.RetentionDays);
                    if (deleted > 0)
                    {
                        _log.Debug($"[PriceTracking] Cleaned up {deleted} old records (time-based)");
                    }
                    break;

                case PriceRetentionType.BySize:
                    var maxBytes = settings.RetentionSizeMb * 1024L * 1024L;
                    var deletedBySize = DbService.CleanupPriceDataBySize(maxBytes);
                    if (deletedBySize > 0)
                    {
                        _log.Debug($"[PriceTracking] Cleaned up {deletedBySize} records (size-based)");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] Cleanup error: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Fetches prices for all items in the player's inventories.
    /// Uses batch database writes for better performance.
    /// </summary>
    public async Task FetchInventoryPricesAsync()
    {
        if (!Settings.AutoFetchInventoryPrices) return;

        try
        {
            var allCaches = _inventoryCacheService.GetAllInventories();
            var itemIds = allCaches
                .SelectMany(c => c.Items.Select(i => (int)i.ItemId))
                .Distinct()
                .Where(id => _marketableItems?.Contains(id) ?? true)
                .Take(100) // Limit to avoid rate limiting
                .ToList();

            if (itemIds.Count == 0) return;

            var scope = _universalisService.GetConfiguredScope();
            if (string.IsNullOrEmpty(scope)) return;

            var wid = _worldData?.GetWorldId(scope);
            if (!wid.HasValue) return;

            _log.Debug($"[PriceTracking] Fetching prices for {itemIds.Count} inventory items");

            // Fetch in batches of 100
            foreach (var batch in itemIds.Chunk(100))
            {
                var data = await _universalisService.GetAggregatedDataAsync(scope, batch.Select(i => (uint)i));
                if (data?.Results == null) continue;

                // Collect all prices for batch save
                var pricesToSave = data.Results.Select(result =>
                {
                    var nqPrice = result.Nq?.MinListing?.World?.Price ?? 0;
                    var hqPrice = result.Hq?.MinListing?.World?.Price ?? 0;
                    var lastSaleNq = result.Nq?.RecentPurchase?.World?.Price ?? 0;
                    var lastSaleHq = result.Hq?.RecentPurchase?.World?.Price ?? 0;
                    return (result.ItemId, wid.Value, nqPrice, hqPrice, lastSaleNq, lastSaleHq);
                }).ToList();

                // Batch save to reduce lock contention
                DbService.SaveItemPricesBatch(pricesToSave);

                // Rate limiting - wait between batches
                await Task.Delay(100);
            }

            _log.Debug("[PriceTracking] Finished fetching inventory prices");
        }
        catch (Exception ex)
        {
            _log.Warning($"[PriceTracking] Error fetching inventory prices: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches prices for inventory items that have stale or missing sale data.
    /// Only fetches items where the last update is more than 5 minutes old.
    /// Uses batch database writes for better performance.
    /// </summary>
    private async Task FetchStaleInventoryPricesAsync()
    {
        try
        {
            // Get inventory item IDs and scope on main thread via framework
            List<int>? allItemIds = null;
            string? scope = null;
            
            await _framework.RunOnFrameworkThread(() =>
            {
                var allCaches = _inventoryCacheService.GetAllInventories();
                allItemIds = allCaches
                    .SelectMany(c => c.Items.Select(i => (int)i.ItemId))
                    .Distinct()
                    .Where(id => _marketableItems?.Contains(id) ?? true)
                    .ToList();
                
                scope = _universalisService.GetConfiguredScope();
            });

            if (allItemIds == null || allItemIds.Count == 0)
            {
                _log.Debug("[PriceTracking] No inventory items to check for stale prices");
                return;
            }

            // Get items with stale or missing sale data (older than 5 minutes)
            var staleThreshold = TimeSpan.FromMinutes(5);
            var staleItemIds = DbService.GetStaleItemIds(allItemIds, staleThreshold);

            if (staleItemIds.Count == 0)
            {
                _log.Debug("[PriceTracking] All inventory items have fresh price data");
                return;
            }

            if (string.IsNullOrEmpty(scope))
            {
                _log.Debug("[PriceTracking] No scope configured, skipping stale price fetch");
                return;
            }

            var wid = _worldData?.GetWorldId(scope);
            if (!wid.HasValue)
            {
                _log.Debug("[PriceTracking] No world ID for scope, skipping stale price fetch");
                return;
            }

            _log.Debug($"[PriceTracking] Fetching prices for {staleItemIds.Count} stale inventory items");

            // Fetch in batches of 100
            var staleItemsList = staleItemIds.ToList();
            foreach (var batch in staleItemsList.Chunk(100))
            {
                if (_disposed) break;

                var data = await _universalisService.GetAggregatedDataAsync(scope, batch.Select(i => (uint)i));
                if (data?.Results == null) continue;

                // Collect all prices for batch save
                var pricesToSave = data.Results.Select(result =>
                {
                    var nqPrice = result.Nq?.MinListing?.World?.Price ?? 0;
                    var hqPrice = result.Hq?.MinListing?.World?.Price ?? 0;
                    var lastSaleNq = result.Nq?.RecentPurchase?.World?.Price ?? 0;
                    var lastSaleHq = result.Hq?.RecentPurchase?.World?.Price ?? 0;
                    return (result.ItemId, wid.Value, nqPrice, hqPrice, lastSaleNq, lastSaleHq);
                }).ToList();

                // Batch save to reduce lock contention
                DbService.SaveItemPricesBatch(pricesToSave);

                // Rate limiting - wait between batches
                await Task.Delay(100);
            }

            _log.Debug($"[PriceTracking] Finished fetching stale inventory prices ({staleItemIds.Count} items)");
        }
        catch (Exception ex)
        {
            _log.Warning($"[PriceTracking] Error fetching stale inventory prices: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the top items by value for a character or all characters.
    /// </summary>
    public async Task<List<(int ItemId, long Quantity, long Value)>> GetTopItemsByValueAsync(
        ulong? characterId = null,
        int maxItems = 100,
        bool includeRetainers = true)
    {
        var result = new List<(int, long, long)>();

        try
        {
            IEnumerable<Models.Inventory.InventoryCacheEntry> caches;

            if (characterId.HasValue)
            {
                caches = _inventoryCacheService.GetInventoriesForCharacter(characterId.Value);
            }
            else
            {
                caches = _inventoryCacheService.GetAllInventories();
            }

            // Aggregate item quantities
            var itemQuantities = new Dictionary<int, long>();

            foreach (var cache in caches)
            {
                if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                    continue;

                foreach (var item in cache.Items)
                {
                    if (_marketableItems != null && !_marketableItems.Contains((int)item.ItemId))
                        continue;

                    if (!itemQuantities.ContainsKey((int)item.ItemId))
                        itemQuantities[(int)item.ItemId] = 0;

                    itemQuantities[(int)item.ItemId] += item.Quantity;
                }
            }

            // Get prices using filtered sale records
            var excludedWorldIds = Settings.ExcludedWorldIds.Count > 0 
                ? Settings.ExcludedWorldIds 
                : null;
            var prices = DbService.GetLatestSalePrices(itemQuantities.Keys, excludedWorldIds);

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
            _log.Debug($"[PriceTracking] Error getting top items: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Enables or disables price tracking.
    /// </summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        Settings.Enabled = enabled;
        _configService.Save();

        if (enabled)
        {
            await _webSocketService.StartAsync();
            await _webSocketService.SubscribeToAllAsync();
        }
        else
        {
            await _webSocketService.StopAsync();
        }
    }

    /// <summary>
    /// Reconnects the WebSocket to apply updated channel subscriptions.
    /// </summary>
    public async Task ReconnectWebSocketAsync()
    {
        if (!Settings.Enabled) return;

        _log.Debug("[PriceTracking] Reconnecting WebSocket to apply channel subscription changes...");
        await _webSocketService.StopAsync();
        _webSocketService.ClearSubscribedChannels();
        await _webSocketService.StartAsync();
        await _webSocketService.SubscribeToAllAsync();
    }

    /// <summary>
    /// Resets all Universalis data - clears price cache and database tables.
    /// </summary>
    public bool ResetAllData()
    {
        try
        {
            _log.Debug("[PriceTracking] Resetting all Universalis data...");

            // Clear in-memory cache
            _priceCache.Clear();

            // Clear database tables
            var result = DbService.ClearAllPriceData();

            if (result)
            {
                _log.Info("[PriceTracking] All Universalis data has been reset");
            }
            else
            {
                _log.Warning("[PriceTracking] Failed to reset Universalis data");
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.Error($"[PriceTracking] Error resetting data: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        
        try { _cts.Cancel(); }
        catch (Exception) { /* Ignore */ }
        
        _framework.Update -= OnFrameworkUpdate;
        _webSocketService.OnPriceUpdate -= OnPriceUpdate;
        
        try { _cts.Dispose(); }
        catch (Exception) { /* Ignore */ }
    }
}
