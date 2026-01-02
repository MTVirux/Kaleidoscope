using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Universalis;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service for caching lowest listings for items across worlds.
/// Receives real-time updates from the WebSocket and backfills stale/missing data from the API.
/// </summary>
public sealed class ListingsService : IDisposable, IService
{
    /// <summary>Threshold for considering cached listings stale (10 minutes).</summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(10);

    /// <summary>Maximum items to backfill per API batch.</summary>
    private const int MaxBackfillBatchSize = 100;

    /// <summary>Delay between API batches to avoid rate limiting.</summary>
    private const int BackfillBatchDelayMs = 200;

    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly ConfigurationService _configService;
    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly InventoryCacheService _inventoryCacheService;

    // In-memory cache: (itemId, worldId) -> ListingsCacheEntry
    private readonly ConcurrentDictionary<(int itemId, int worldId), ListingsCacheEntry> _listingsCache = new();

    private PriceTrackingSettings Settings => _configService.Config.PriceTracking;

    private volatile bool _disposed;
    private volatile bool _initialized;
    private CancellationTokenSource? _backfillCts;

    /// <summary>Gets whether the service has completed initial backfill.</summary>
    public bool IsInitialized => _initialized;

    /// <summary>Gets the number of cached listings.</summary>
    public int CacheCount => _listingsCache.Count;

    /// <summary>Event fired when a listing is updated.</summary>
    public event Action<int, int>? OnListingUpdated;

    public ListingsService(
        IPluginLog log,
        IFramework framework,
        ConfigurationService configService,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        InventoryCacheService inventoryCacheService)
    {
        _log = log;
        _framework = framework;
        _configService = configService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _inventoryCacheService = inventoryCacheService;

        _webSocketService.OnPriceUpdate += OnPriceUpdate;

        LogService.Debug(LogCategory.Listings, "[ListingsService] Service initialized");
    }

    /// <summary>
    /// Initializes the service by backfilling stale/missing listings from the API.
    /// Should be called after PriceTrackingService has loaded world data and marketable items.
    /// </summary>
    public async Task InitializeAsync(UniversalisWorldData? worldData, IReadOnlySet<int>? marketableItems)
    {
        if (_disposed || _initialized) return;

        try
        {
            LogService.Debug(LogCategory.Listings, "[ListingsService] Starting initialization and backfill");
            _backfillCts = new CancellationTokenSource();

            await BackfillStaleListingsAsync(worldData, marketableItems, _backfillCts.Token);

            _initialized = true;
            LogService.Debug(LogCategory.Listings, "[ListingsService] Initialization complete");
        }
        catch (OperationCanceledException)
        {
            LogService.Debug(LogCategory.Listings, "[ListingsService] Backfill cancelled");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Listings, $"[ListingsService] Initialization failed: {ex.Message}");
        }
    }

    private void OnPriceUpdate(PriceFeedEntry entry)
    {
        try
        {
            // Only process listing events (not sales)
            if (entry.EventType == "Sale") return;

            UpdateListingFromWebSocket(entry.ItemId, entry.WorldId, entry.PricePerUnit, entry.IsHq);
        }
        catch (Exception ex)
        {
            LogService.Verbose(LogCategory.Listings, $"[ListingsService] Error processing price update: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the cached listing from a WebSocket event.
    /// For "Listing Added" events, adds the price to the sorted list.
    /// For "Listing Removed" events, we can't reliably update (would need full refresh).
    /// </summary>
    private void UpdateListingFromWebSocket(int itemId, int worldId, int price, bool isHq)
    {
        if (price <= 0) return;

        var key = (itemId, worldId);
        var now = DateTime.UtcNow;

        _listingsCache.AddOrUpdate(
            key,
            // Add new entry
            _ =>
            {
                var entry = new ListingsCacheEntry
                {
                    ItemId = itemId,
                    WorldId = worldId,
                    LastUpdated = now
                };
                entry.AddPrice(price, isHq);
                return entry;
            },
            // Update existing entry - add to the sorted price list
            (_, existing) =>
            {
                existing.AddPrice(price, isHq);
                return existing;
            });

        OnListingUpdated?.Invoke(itemId, worldId);
    }

    /// <summary>
    /// Updates listings from API data with full listing prices.
    /// </summary>
    private void UpdateListingsFromApi(int itemId, int worldId, IEnumerable<int> nqPrices, IEnumerable<int> hqPrices)
    {
        var key = (itemId, worldId);
        var now = DateTime.UtcNow;

        var entry = new ListingsCacheEntry
        {
            ItemId = itemId,
            WorldId = worldId,
            LastUpdated = now
        };
        entry.SetPrices(nqPrices, isHq: false);
        entry.SetPrices(hqPrices, isHq: true);

        _listingsCache[key] = entry;
    }

    /// <summary>
    /// Updates listings from API data with single min prices (legacy compatibility).
    /// </summary>
    private void UpdateListingsFromApiSingle(int itemId, int worldId, int minPriceNq, int minPriceHq)
    {
        var nqPrices = minPriceNq > 0 ? new[] { minPriceNq } : Array.Empty<int>();
        var hqPrices = minPriceHq > 0 ? new[] { minPriceHq } : Array.Empty<int>();
        UpdateListingsFromApi(itemId, worldId, nqPrices, hqPrices);
    }

    /// <summary>
    /// Gets the cached listing for an item on a specific world.
    /// Returns null if not cached.
    /// </summary>
    public ListingsCacheEntry? GetListing(int itemId, int worldId)
    {
        return _listingsCache.TryGetValue((itemId, worldId), out var entry) ? entry : null;
    }

    /// <summary>
    /// Gets the cached listing for an item on any world (returns the lowest price across all worlds).
    /// Returns null if not cached.
    /// </summary>
    public ListingsCacheEntry? GetLowestListingAcrossWorlds(int itemId)
    {
        ListingsCacheEntry? lowest = null;
        var lowestPrice = int.MaxValue;

        foreach (var kvp in _listingsCache)
        {
            if (kvp.Key.itemId != itemId) continue;
            
            var entry = kvp.Value;
            var price = entry.LowestPrice;
            if (price > 0 && price < lowestPrice)
            {
                lowestPrice = price;
                lowest = entry;
            }
        }

        return lowest;
    }

    /// <summary>
    /// Gets all cached listings for an item across all worlds.
    /// </summary>
    public IEnumerable<ListingsCacheEntry> GetListingsForItem(int itemId)
    {
        foreach (var kvp in _listingsCache)
        {
            if (kvp.Key.itemId == itemId)
            {
                yield return kvp.Value;
            }
        }
    }

    /// <summary>
    /// Gets all cached listings for a specific world.
    /// </summary>
    public IEnumerable<ListingsCacheEntry> GetListingsForWorld(int worldId)
    {
        foreach (var kvp in _listingsCache)
        {
            if (kvp.Key.worldId == worldId)
            {
                yield return kvp.Value;
            }
        }
    }

    /// <summary>
    /// Checks if an item has fresh (non-stale) listing data.
    /// </summary>
    public bool HasFreshListing(int itemId, int worldId)
    {
        if (!_listingsCache.TryGetValue((itemId, worldId), out var entry))
            return false;

        return !entry.IsStale(StaleThreshold);
    }

    /// <summary>
    /// Gets item IDs that need to be fetched from the API (missing or stale).
    /// </summary>
    /// <param name="itemIds">Item IDs to check.</param>
    /// <param name="worldId">World ID to check listings for.</param>
    /// <returns>List of item IDs that are missing or have stale listings.</returns>
    public List<int> GetStaleOrMissingItems(IEnumerable<int> itemIds, int worldId)
    {
        var staleItems = new List<int>();

        foreach (var itemId in itemIds)
        {
            var key = (itemId, worldId);
            if (!_listingsCache.TryGetValue(key, out var entry) || entry.IsStale(StaleThreshold))
            {
                staleItems.Add(itemId);
            }
        }

        return staleItems;
    }

    /// <summary>
    /// Backfills stale or missing listings from the Universalis API.
    /// Called at startup and can be called periodically.
    /// Uses the same scope configuration as the WebSocket subscription.
    /// </summary>
    private async Task BackfillStaleListingsAsync(
        UniversalisWorldData? worldData,
        IReadOnlySet<int>? marketableItems,
        CancellationToken ct)
    {
        if (worldData == null)
        {
            LogService.Debug(LogCategory.Listings, "[ListingsService] No world data available for backfill");
            return;
        }

        // Get inventory items on the framework thread
        var inventoryItems = new List<int>();
        await _framework.RunOnFrameworkThread(() =>
        {
            var caches = _inventoryCacheService.GetAllInventories();
            foreach (var cache in caches)
            {
                foreach (var item in cache.Items)
                {
                    if (marketableItems == null || marketableItems.Contains((int)item.ItemId))
                    {
                        inventoryItems.Add((int)item.ItemId);
                    }
                }
            }
        });

        var allItemIds = inventoryItems.Distinct().ToList();

        if (allItemIds.Count == 0)
        {
            LogService.Debug(LogCategory.Listings, "[ListingsService] No inventory items to backfill");
            return;
        }

        // Use the WebSocket subscription scope settings for backfill
        var settings = Settings;
        var scopeAndWorldIds = GetBackfillScopesFromSettings(settings, worldData);

        if (scopeAndWorldIds.Count == 0)
        {
            LogService.Debug(LogCategory.Listings, "[ListingsService] No scope configured for backfill");
            return;
        }

        var fetched = 0;

        // Backfill for each scope (world/DC/region)
        foreach (var (scope, worldIds) in scopeAndWorldIds)
        {
            if (ct.IsCancellationRequested) break;

            // Find items that are stale or missing for any of the worlds in this scope
            var itemsToFetch = new HashSet<int>();
            foreach (var worldId in worldIds)
            {
                foreach (var itemId in GetStaleOrMissingItems(allItemIds, worldId))
                {
                    itemsToFetch.Add(itemId);
                }
            }

            if (itemsToFetch.Count == 0)
            {
                LogService.Debug(LogCategory.Listings, $"[ListingsService] All inventory items have fresh listings for scope '{scope}'");
                continue;
            }

            LogService.Debug(LogCategory.Listings, $"[ListingsService] Backfilling {itemsToFetch.Count} stale/missing listings for scope '{scope}'...");

            // Fetch in batches
            var batchNumber = 0;
            var totalBatches = (int)Math.Ceiling(itemsToFetch.Count / (double)MaxBackfillBatchSize);
            foreach (var batch in itemsToFetch.Chunk(MaxBackfillBatchSize))
            {
                if (ct.IsCancellationRequested) break;
                batchNumber++;

                try
                {
                    LogService.Debug(LogCategory.Listings, $"[ListingsService] Fetching batch {batchNumber}/{totalBatches} ({batch.Length} items) for scope '{scope}'...");

                    var data = await _universalisService.GetMarketBoardDataAsync(
                        scope,
                        batch.Select(i => (uint)i),
                        listings: ListingsCacheEntry.MaxPricesPerType, // Get 5 listings for averaging
                        entries: 0,  // No history needed
                        cancellationToken: ct);

                    if (data?.Items != null)
                    {
                        foreach (var kvp in data.Items)
                        {
                            if (int.TryParse(kvp.Key, out var itemId))
                            {
                                var marketData = kvp.Value;
                                // Extract prices from listings if available, otherwise use min prices
                                var nqPrices = marketData.Listings?
                                    .Where(l => !l.IsHq)
                                    .OrderBy(l => l.PricePerUnit)
                                    .Take(ListingsCacheEntry.MaxPricesPerType)
                                    .Select(l => l.PricePerUnit)
                                    .ToList() ?? (marketData.MinPriceNQ > 0 ? new List<int> { marketData.MinPriceNQ } : new List<int>());
                                
                                var hqPrices = marketData.Listings?
                                    .Where(l => l.IsHq)
                                    .OrderBy(l => l.PricePerUnit)
                                    .Take(ListingsCacheEntry.MaxPricesPerType)
                                    .Select(l => l.PricePerUnit)
                                    .ToList() ?? (marketData.MinPriceHQ > 0 ? new List<int> { marketData.MinPriceHQ } : new List<int>());

                                // Update listings for each world in the scope
                                foreach (var worldId in worldIds)
                                {
                                    UpdateListingsFromApi(itemId, worldId, nqPrices, hqPrices);
                                }
                                fetched++;
                            }
                        }
                    }

                    // Small delay between batches to avoid rate limiting
                    if (batch.Length == MaxBackfillBatchSize)
                    {
                        await Task.Delay(BackfillBatchDelayMs, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogService.Warning(LogCategory.Listings, $"[ListingsService] Error fetching batch for scope '{scope}': {ex.Message}");
                }
            }
        }

        LogService.Debug(LogCategory.Listings, $"[ListingsService] Backfill complete, fetched {fetched} listings from API");
    }

    /// <summary>
    /// Gets the scopes and corresponding world IDs for backfilling based on WebSocket subscription settings.
    /// </summary>
    private List<(string scope, List<int> worldIds)> GetBackfillScopesFromSettings(
        PriceTrackingSettings settings,
        UniversalisWorldData worldData)
    {
        var result = new List<(string scope, List<int> worldIds)>();

        switch (settings.ScopeMode)
        {
            case PriceTrackingScopeMode.All:
                // For "All" mode, use the default configured scope from UniversalisService
                var defaultScope = _universalisService.GetConfiguredScope();
                if (!string.IsNullOrEmpty(defaultScope))
                {
                    var worldId = worldData.GetWorldId(defaultScope);
                    if (worldId.HasValue)
                    {
                        result.Add((defaultScope, new List<int> { worldId.Value }));
                    }
                    else
                    {
                        // It might be a DC or region name, get all world IDs for it
                        var worldIds = GetWorldIdsForScope(defaultScope, worldData);
                        if (worldIds.Count > 0)
                        {
                            result.Add((defaultScope, worldIds));
                        }
                    }
                }
                break;

            case PriceTrackingScopeMode.ByWorld:
                foreach (var worldId in settings.SelectedWorldIds)
                {
                    var worldName = worldData.GetWorldName(worldId);
                    if (!string.IsNullOrEmpty(worldName))
                    {
                        result.Add((worldName, new List<int> { worldId }));
                    }
                }
                break;

            case PriceTrackingScopeMode.ByDataCenter:
                foreach (var dcName in settings.SelectedDataCenters)
                {
                    var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                    if (dc?.Worlds != null && dc.Worlds.Count > 0)
                    {
                        result.Add((dcName, dc.Worlds.ToList()));
                    }
                }
                break;

            case PriceTrackingScopeMode.ByRegion:
                foreach (var regionName in settings.SelectedRegions)
                {
                    var worldIds = new List<int>();
                    foreach (var dc in worldData.GetDataCentersForRegion(regionName))
                    {
                        if (dc.Worlds != null)
                        {
                            worldIds.AddRange(dc.Worlds);
                        }
                    }
                    if (worldIds.Count > 0)
                    {
                        result.Add((regionName, worldIds));
                    }
                }
                break;
        }

        return result;
    }

    /// <summary>
    /// Gets all world IDs for a scope string (which could be a world name, DC name, or region name).
    /// </summary>
    private List<int> GetWorldIdsForScope(string scope, UniversalisWorldData worldData)
    {
        var worldIds = new List<int>();

        // Check if it's a world name
        var worldId = worldData.GetWorldId(scope);
        if (worldId.HasValue)
        {
            worldIds.Add(worldId.Value);
            return worldIds;
        }

        // Check if it's a data center name
        var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == scope);
        if (dc?.Worlds != null)
        {
            worldIds.AddRange(dc.Worlds);
            return worldIds;
        }

        // Check if it's a region name
        foreach (var regionDc in worldData.GetDataCentersForRegion(scope))
        {
            if (regionDc.Worlds != null)
            {
                worldIds.AddRange(regionDc.Worlds);
            }
        }

        return worldIds;
    }

    /// <summary>
    /// Manually triggers a backfill for specific items.
    /// Uses the world name for the given world ID as the scope.
    /// </summary>
    public async Task BackfillItemsAsync(IEnumerable<int> itemIds, int worldId, UniversalisWorldData? worldData = null, CancellationToken ct = default)
    {
        // Try to get the world name for the scope, or fall back to the configured scope
        string? scope = null;
        if (worldData != null)
        {
            scope = worldData.GetWorldName(worldId);
        }
        scope ??= _universalisService.GetConfiguredScope();

        if (string.IsNullOrEmpty(scope))
        {
            LogService.Debug(LogCategory.Listings, "[ListingsService] No scope configured for manual backfill");
            return;
        }

        var itemList = itemIds.ToList();
        if (itemList.Count == 0) return;

        LogService.Debug(LogCategory.Listings, $"[ListingsService] Manual backfill for {itemList.Count} items from API...");

        foreach (var batch in itemList.Chunk(MaxBackfillBatchSize))
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var data = await _universalisService.GetMarketBoardDataAsync(
                    scope,
                    batch.Select(i => (uint)i),
                    listings: ListingsCacheEntry.MaxPricesPerType, // Get 5 listings for averaging
                    entries: 0,
                    cancellationToken: ct);

                if (data?.Items != null)
                {
                    foreach (var kvp in data.Items)
                    {
                        if (int.TryParse(kvp.Key, out var itemId))
                        {
                            var marketData = kvp.Value;
                            // Extract prices from listings if available
                            var nqPrices = marketData.Listings?
                                .Where(l => !l.IsHq)
                                .OrderBy(l => l.PricePerUnit)
                                .Take(ListingsCacheEntry.MaxPricesPerType)
                                .Select(l => l.PricePerUnit)
                                .ToList() ?? (marketData.MinPriceNQ > 0 ? new List<int> { marketData.MinPriceNQ } : new List<int>());
                            
                            var hqPrices = marketData.Listings?
                                .Where(l => l.IsHq)
                                .OrderBy(l => l.PricePerUnit)
                                .Take(ListingsCacheEntry.MaxPricesPerType)
                                .Select(l => l.PricePerUnit)
                                .ToList() ?? (marketData.MinPriceHQ > 0 ? new List<int> { marketData.MinPriceHQ } : new List<int>());

                            UpdateListingsFromApi(itemId, worldId, nqPrices, hqPrices);
                        }
                    }
                }

                if (batch.Length == MaxBackfillBatchSize)
                {
                    await Task.Delay(BackfillBatchDelayMs, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning(LogCategory.Listings, $"[ListingsService] Error in manual backfill: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears all cached listings.
    /// </summary>
    public void ClearCache()
    {
        _listingsCache.Clear();
        LogService.Debug(LogCategory.Listings, "[ListingsService] Cache cleared");
    }

    public void Dispose()
    {
        _disposed = true;

        _webSocketService.OnPriceUpdate -= OnPriceUpdate;

        try { _backfillCts?.Cancel(); }
        catch (Exception) { /* Ignore */ }

        try { _backfillCts?.Dispose(); }
        catch (Exception) { /* Ignore */ }

        LogService.Debug(LogCategory.Listings, "[ListingsService] Disposed");
    }
}
