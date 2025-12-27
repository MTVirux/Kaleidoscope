using System.Collections.Concurrent;
using System.Threading.Channels;
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
    #region Fields and Properties
    
    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly ConfigurationService _configService;
    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly SamplerService _samplerService;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly ListingsService _listingsService;
    private readonly ItemDataService _itemDataService;
    private readonly TimeSeriesCacheService _cacheService;

    // Cached world/DC data from Universalis
    private UniversalisWorldData? _worldData;
    private HashSet<int>? _marketableItems;
    private DateTime _lastWorldDataFetch = DateTime.MinValue;
    private DateTime _lastMarketableItemsFetch = DateTime.MinValue;
    private DateTime _lastCleanup = DateTime.MinValue;
    private DateTime _lastValueSnapshot = DateTime.MinValue;

    // Event-driven inventory value sampling state
    private DateTime _lastEventDrivenValueSample = DateTime.MinValue;
    private volatile bool _pendingValueRecalc = false;
    private readonly HashSet<int> _pendingPriceUpdateItemIds = new();
    private readonly object _pendingLock = new();
    private const int EventDrivenSampleThrottleSeconds = 30; // Min seconds between event-driven samples

    // In-memory price cache for quick lookups
    private readonly ConcurrentDictionary<(int itemId, int worldId), (int minNq, int minHq, DateTime updated)> _priceCache = new();
    
    // In-memory cache for recent sale prices (used for spike detection without DB reads)
    // Key: (itemId, isHq), Value: last sale price
    private readonly ConcurrentDictionary<(int itemId, bool isHq), int> _lastSalePriceCache = new();
    // Key: (itemId, worldId, isHq), Value: last sale price for specific world
    private readonly ConcurrentDictionary<(int itemId, int worldId, bool isHq), int> _lastSalePriceByWorldCache = new();
    
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disposed;

    // Background thread for database writes from WebSocket updates
    private readonly Channel<PriceUpdateWorkItem> _priceUpdateQueue;
    private readonly Task _backgroundWorker;

    private const int WorldDataRefreshHours = 24;
    private const int MarketableItemsRefreshHours = 24;
    private const int ValueSnapshotIntervalMinutes = 15;

    private PriceTrackingSettings Settings => _configService.Config.PriceTracking;
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    /// <summary>Gets the cached world data.</summary>
    public UniversalisWorldData? WorldData => _worldData;

    /// <summary>Gets the Universalis service for API calls.</summary>
    public UniversalisService UniversalisService => _universalisService;

    /// <summary>Gets the listings cache service for lowest listing lookups.</summary>
    public ListingsService ListingsService => _listingsService;

    /// <summary>Gets the set of marketable item IDs.</summary>
    public IReadOnlySet<int>? MarketableItems => _marketableItems;

    /// <summary>Gets whether the service is initialized.</summary>
    public bool IsInitialized => _worldData != null && _marketableItems != null;

    /// <summary>Gets whether the WebSocket is currently connected for real-time price updates.</summary>
    public bool IsSocketConnected => _webSocketService.IsConnected;

    /// <summary>Event fired when price data is updated (new price received via WebSocket).</summary>
    public event Action<int>? OnPriceDataUpdated;
    
    /// <summary>Event fired when world data is loaded/refreshed from Universalis.</summary>
    public event Action? OnWorldDataLoaded;

    #endregion

    #region Constructor and Initialization

    public PriceTrackingService(
        IPluginLog log,
        IFramework framework,
        ConfigurationService configService,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        SamplerService samplerService,
        InventoryCacheService inventoryCacheService,
        ListingsService listingsService,
        ItemDataService itemDataService,
        TimeSeriesCacheService cacheService)
    {
        _log = log;
        _framework = framework;
        _configService = configService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _samplerService = samplerService;
        _inventoryCacheService = inventoryCacheService;
        _listingsService = listingsService;
        _itemDataService = itemDataService;
        _cacheService = cacheService;

        // Initialize background work queue for WebSocket price updates (unbounded, single consumer)
        _priceUpdateQueue = Channel.CreateUnbounded<PriceUpdateWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Start the background worker thread for database writes
        _backgroundWorker = Task.Factory.StartNew(
            ProcessPriceUpdateQueueAsync,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        ).Unwrap();

        // Subscribe to WebSocket events
        _webSocketService.OnPriceUpdate += OnPriceUpdate;

        // Subscribe to framework update for periodic tasks
        _framework.Update += OnFrameworkUpdate;

        _log.Debug("[PriceTracking] Service initialized with background thread for price updates");

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
            
            // Pre-populate inventory value cache on background thread
            // This prevents blocking on main thread when InventoryValueTool first draws
            PopulateInventoryValueCache();
            
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
                
                // Initialize the listings service with world data and marketable items
                await _listingsService.InitializeAsync(_worldData, _marketableItems);
                
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

    #endregion

    #region Framework Update and Events

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_disposed)
            return;
        
        var now = DateTime.UtcNow;

        // Event-driven value sampling (triggered by price updates or inventory changes)
        if (_pendingValueRecalc && 
            (now - _lastEventDrivenValueSample).TotalSeconds >= EventDrivenSampleThrottleSeconds &&
            Settings.Enabled)
        {
            _pendingValueRecalc = false;
            _lastEventDrivenValueSample = now;
            _ = Task.Run(TakeEventDrivenValueSnapshotsAsync);
        }

        // Periodic cleanup
        if ((now - _lastCleanup).TotalMinutes >= Settings.CleanupIntervalMinutes)
        {
            _lastCleanup = now;
            _ = Task.Run(PerformCleanupAsync);
        }

        // Periodic value snapshots (fallback for when no events trigger updates)
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
                // Skip sales from mannequins
                if (entry.OnMannequin)
                {
                    var itemName = _itemDataService.GetItemName(entry.ItemId);
                    _log.Verbose($"[PriceTracking] Ignoring mannequin sale for {itemName} ({entry.ItemId})");
                    return;
                }

                // Check for price spikes (100x or higher than previous sale) only for items with previous sales >= 10k
                // Uses in-memory cache to avoid blocking DB reads on the WebSocket thread
                _lastSalePriceCache.TryGetValue((entry.ItemId, entry.IsHq), out var previousPrice);
                if (previousPrice >= 10000 && entry.PricePerUnit >= (long)previousPrice * 100)
                {
                    var itemName = _itemDataService.GetItemName(entry.ItemId);
                    _log.Debug($"[PriceTracking] Ignoring price spike for {itemName} ({entry.ItemId}): {entry.PricePerUnit:N0} is 100x+ higher than previous {previousPrice:N0}");
                    return;
                }

                // Check for listing price discrepancy if enabled
                // Uses average of lowest listing and most recent sale for that world as reference
                // Skip the filter if the unit price is below the minimum threshold
                if (Settings.FilterSalesByListingPrice && entry.PricePerUnit >= Settings.SaleFilterMinimumPrice)
                {
                    var listing = _listingsService.GetListing(entry.ItemId, entry.WorldId);
                    var listingPrice = listing != null ? (entry.IsHq ? listing.MinPriceHq : listing.MinPriceNq) : 0;
                    // Use in-memory cache instead of DB read
                    _lastSalePriceByWorldCache.TryGetValue((entry.ItemId, entry.WorldId, entry.IsHq), out var recentSalePrice);
                    
                    // Calculate reference price as average of listing and recent sale (if both available)
                    double referencePrice;
                    if (listingPrice > 0 && recentSalePrice > 0)
                        referencePrice = (listingPrice + recentSalePrice) / 2.0;
                    else if (listingPrice > 0)
                        referencePrice = listingPrice;
                    else if (recentSalePrice > 0)
                        referencePrice = recentSalePrice;
                    else
                        referencePrice = 0; // No reference data available
                    
                    if (referencePrice > 0)
                    {
                        var ratio = entry.PricePerUnit / referencePrice;
                        var threshold = Settings.SaleDiscrepancyThreshold / 100.0;
                        var minRatio = 1.0 - threshold;
                        var maxRatio = 1.0 + threshold;
                        if (ratio < minRatio || ratio > maxRatio)
                        {
                            var itemName = _itemDataService.GetItemName(entry.ItemId);
                            var worldName = _worldData?.GetWorldName(entry.WorldId) ?? entry.WorldId.ToString();
                            _log.Debug($"[PriceTracking] Ignoring sale for {itemName} on {worldName}: " +
                                $"price {entry.PricePerUnit:N0} is {(ratio * 100 - 100):+0;-0}% from reference {referencePrice:N0} (listing: {listingPrice:N0}, recent sale: {recentSalePrice:N0}, threshold: {Settings.SaleDiscrepancyThreshold}%)");
                            return;
                        }
                    }
                }

                // Sale event - queue write to background thread
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

                // Queue the write to background thread
                _priceUpdateQueue.Writer.TryWrite(new PriceUpdateWorkItem(
                    ItemId: entry.ItemId,
                    WorldId: entry.WorldId,
                    IsSale: true,
                    PricePerUnit: entry.PricePerUnit,
                    Quantity: entry.Quantity,
                    IsHq: entry.IsHq,
                    Total: entry.Total,
                    BuyerName: entry.BuyerName,
                    ExistingMinNq: existingNq,
                    ExistingMinHq: existingHq,
                    LastSaleNq: lastSaleNq,
                    LastSaleHq: lastSaleHq,
                    CachedMinNq: 0,
                    CachedMinHq: 0
                ));
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

                // Queue to database write on background thread
                if (_priceCache.TryGetValue(key, out var cached))
                {
                    _priceUpdateQueue.Writer.TryWrite(new PriceUpdateWorkItem(
                        ItemId: entry.ItemId,
                        WorldId: entry.WorldId,
                        IsSale: false,
                        PricePerUnit: entry.PricePerUnit,
                        Quantity: entry.Quantity,
                        IsHq: entry.IsHq,
                        Total: entry.Total,
                        BuyerName: null,
                        ExistingMinNq: 0,
                        ExistingMinHq: 0,
                        LastSaleNq: 0,
                        LastSaleHq: 0,
                        CachedMinNq: cached.minNq,
                        CachedMinHq: cached.minHq
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] Error processing price update: {ex.Message}");
        }
    }

    #endregion

    #region Background Processing

    /// <summary>
    /// Background worker that processes queued price updates.
    /// Drains the channel in batches and writes to the database on a dedicated thread.
    /// Uses batching to reduce lock contention with the main thread.
    /// </summary>
    private async Task ProcessPriceUpdateQueueAsync()
    {
        const int BatchSize = 50;
        const int BatchDelayMs = 100; // Wait up to 100ms to collect more items
        
        var batch = new List<PriceUpdateWorkItem>(BatchSize);
        var itemsToNotify = new HashSet<int>();
        
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                batch.Clear();
                itemsToNotify.Clear();
                
                // Wait for at least one item
                if (!await _priceUpdateQueue.Reader.WaitToReadAsync(_cts.Token))
                    break; // Channel completed
                
                // Collect items for up to BatchDelayMs or until batch is full
                var batchDeadline = DateTime.UtcNow.AddMilliseconds(BatchDelayMs);
                while (batch.Count < BatchSize && DateTime.UtcNow < batchDeadline)
                {
                    if (_priceUpdateQueue.Reader.TryRead(out var workItem))
                    {
                        batch.Add(workItem);
                    }
                    else if (batch.Count > 0)
                    {
                        // No more items available, process what we have
                        break;
                    }
                    else
                    {
                        // Wait a bit for more items
                        await Task.Delay(10, _cts.Token);
                    }
                }
                
                if (batch.Count == 0) continue;
                
                // Process the batch - DB writes are done inside SaveSaleRecordsBatch/SaveItemPricesBatch
                // which use transactions to minimize lock time
                try
                {
                    // Separate sales and listings
                    var sales = batch.Where(w => w.IsSale).ToList();
                    var listings = batch.Where(w => !w.IsSale).ToList();
                    
                    // Process sales
                    if (sales.Count > 0)
                    {
                        // Save sale records in batch
                        var saleRecords = sales.Select(w => (
                            w.ItemId, w.WorldId, w.PricePerUnit, w.Quantity, w.IsHq, w.Total, w.BuyerName
                        )).ToList();
                        DbService.SaveSaleRecordsBatch(saleRecords);
                        
                        // Save item prices in batch
                        var salePrices = sales.Select(w => (
                            w.ItemId, w.WorldId, w.ExistingMinNq, w.ExistingMinHq, w.LastSaleNq, w.LastSaleHq
                        )).ToList();
                        DbService.SaveItemPricesBatch(salePrices);
                        
                        // Update in-memory caches
                        foreach (var w in sales)
                        {
                            _lastSalePriceCache[(w.ItemId, w.IsHq)] = w.PricePerUnit;
                            _lastSalePriceByWorldCache[(w.ItemId, w.WorldId, w.IsHq)] = w.PricePerUnit;
                            itemsToNotify.Add(w.ItemId);
                        }
                    }
                    
                    // Process listings
                    if (listings.Count > 0)
                    {
                        var listingPrices = listings.Select(w => (
                            w.ItemId, w.WorldId, w.CachedMinNq, w.CachedMinHq, 0, 0 // No sale prices for listings
                        )).ToList();
                        DbService.SaveItemPricesBatch(listingPrices);
                        
                        foreach (var w in listings)
                        {
                            itemsToNotify.Add(w.ItemId);
                        }
                    }
                    
                    // Notify listeners for all affected items (deduplicated)
                    foreach (var itemId in itemsToNotify)
                    {
                        OnPriceDataUpdated?.Invoke(itemId);
                    }
                    
                    // Flag pending inventory value recalc if any sale prices updated
                    // (Sales are what we use for inventory valuation)
                    if (sales.Count > 0)
                    {
                        _pendingValueRecalc = true;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Verbose($"[PriceTracking] Background batch write error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            LogService.Error($"[PriceTracking] Background worker crashed: {ex.Message}", ex);
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

    #endregion

    #region World Data and Marketable Items

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
                
                // Notify subscribers that world data is now available
                OnWorldDataLoaded?.Invoke();
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

    #endregion

    #region Price Fetching and Caching

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

    #endregion

    #region Inventory Value Calculation

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
    /// Also queues samples to the standard time-series tracking.
    /// </summary>
    private async Task TakeValueSnapshotsAsync()
    {
        if (_disposed) return;
        
        try
        {
            var characterData = DbService.GetAllCharacterNames();
            var characterIds = characterData
                .Select(c => c.characterId)
                .Distinct()
                .ToList();

            if (characterIds.Count == 0) return;

            // Build a lookup for character names
            var characterNames = characterData.ToDictionary(c => c.characterId, c => c.name);

            var includeRetainers = _configService.Config.InventoryValue.IncludeRetainers;
            
            // Calculate values for all characters in parallel
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
                // Save to inventory_value_history (existing behavior)
                DbService.SaveInventoryValueHistory(charId, total, gil, item, contributions);
                
                // Also queue to standard time-series tracking
                // Only item value - Gil is tracked via Gil currency, Total can be merged in UI
                _samplerService.QueueInventoryValueSample(charId, item, characterName);
            }
            
            // Re-populate the full cache on background thread so main thread doesn't block
            PopulateInventoryValueCache();

            _log.Debug($"[PriceTracking] Saved value snapshots for {characterIds.Count} characters (parallel)");
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] Error taking value snapshots: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes value snapshots triggered by price updates or inventory changes.
    /// Similar to TakeValueSnapshotsAsync but only writes to time-series tables (not inventory_value_history)
    /// to avoid duplicating data. The inventory_value_history is still updated on the 15-minute interval.
    /// </summary>
    private async Task TakeEventDrivenValueSnapshotsAsync()
    {
        if (_disposed) return;
        
        try
        {
            var characterData = DbService.GetAllCharacterNames();
            var characterIds = characterData
                .Select(c => c.characterId)
                .Distinct()
                .ToList();

            if (characterIds.Count == 0) return;

            // Build a lookup for character names
            var characterNames = characterData.ToDictionary(c => c.characterId, c => c.name);

            var includeRetainers = _configService.Config.InventoryValue.IncludeRetainers;
            
            // Calculate values for all characters in parallel
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
                _samplerService.QueueInventoryValueSample(charId, item, characterName);
            }

            _log.Verbose($"[PriceTracking] Event-driven value samples for {characterIds.Count} characters");
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] Error taking event-driven value snapshots: {ex.Message}");
        }
    }

    /// <summary>
    /// Populates the in-memory inventory value cache from the database.
    /// This runs on the background thread so the main thread never hits the DB.
    /// </summary>
    private void PopulateInventoryValueCache()
    {
        try
        {
            var historyData = DbService.GetAllInventoryValueHistory();
            _cacheService.SetInventoryValueCache(historyData);
            _log.Debug($"[PriceTracking] Populated inventory value cache with {historyData.Count} records");
        }
        catch (Exception ex)
        {
            _log.Debug($"[PriceTracking] Error populating inventory value cache: {ex.Message}");
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

    #endregion

    #region Inventory Price Fetching

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

    #endregion

    #region Top Items Analysis

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

    #endregion

    #region Service Control

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

        // Complete the channel to stop the background worker
        _priceUpdateQueue.Writer.TryComplete();

        // Wait for background worker to finish (with timeout)
        try { _backgroundWorker.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* Ignore timeout */ }
        
        _framework.Update -= OnFrameworkUpdate;
        _webSocketService.OnPriceUpdate -= OnPriceUpdate;
        
        try { _cts.Dispose(); }
        catch (Exception) { /* Ignore */ }
    }

    #endregion
}

/// <summary>
/// Work item representing a price update to be persisted to the database.
/// </summary>
/// <param name="ItemId">The item ID.</param>
/// <param name="WorldId">The world ID.</param>
/// <param name="IsSale">Whether this is a sale event (true) or listing event (false).</param>
/// <param name="PricePerUnit">Price per unit.</param>
/// <param name="Quantity">Quantity.</param>
/// <param name="IsHq">Whether the item is HQ.</param>
/// <param name="Total">Total price.</param>
/// <param name="BuyerName">Buyer name (for sales).</param>
/// <param name="ExistingMinNq">Existing cached min NQ price.</param>
/// <param name="ExistingMinHq">Existing cached min HQ price.</param>
/// <param name="LastSaleNq">Last sale NQ price (for sales).</param>
/// <param name="LastSaleHq">Last sale HQ price (for sales).</param>
/// <param name="CachedMinNq">Cached min NQ price after update (for listings).</param>
/// <param name="CachedMinHq">Cached min HQ price after update (for listings).</param>
internal readonly record struct PriceUpdateWorkItem(
    int ItemId,
    int WorldId,
    bool IsSale,
    int PricePerUnit,
    int Quantity,
    bool IsHq,
    int Total,
    string? BuyerName,
    int ExistingMinNq,
    int ExistingMinHq,
    int LastSaleNq,
    int LastSaleHq,
    int CachedMinNq,
    int CachedMinHq
);
