using System.Collections.Concurrent;
using System.Threading.Channels;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Universalis;
using OtterGui.Services;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Database;
using Kaleidoscope.Services.Inventory;

namespace Kaleidoscope.Services.Universalis;

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
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly ListingsService _listingsService;
    private readonly ItemDataService _itemDataService;
    private readonly TimeSeriesCacheService _cacheService;
    private readonly SalePriceCacheService _salePriceCacheService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly CharacterDataCacheService _characterDataCache;

    private UniversalisWorldData? _worldData;
    private HashSet<int>? _marketableItems;
    private DateTime _lastWorldDataFetch = DateTime.MinValue;
    private DateTime _lastMarketableItemsFetch = DateTime.MinValue;
    private DateTime _lastCleanup = DateTime.MinValue;
    private DateTime _lastValueSnapshot = DateTime.MinValue;

    private DateTime _lastEventDrivenValueSample = DateTime.MinValue;
    private volatile bool _pendingValueRecalc = false;
    private readonly HashSet<int> _pendingPriceUpdateItemIds = new();
    private readonly object _pendingLock = new();

    private readonly ConcurrentDictionary<(int itemId, int worldId), (int minNq, int minHq, DateTime updated)> _priceCache = new();
    
    // In-memory cache for recent sale prices (used for spike detection without DB reads)
    // Key: (itemId, isHq), Value: last sale price (global, for spike detection)
    private readonly ConcurrentDictionary<(int itemId, bool isHq), int> _lastSalePriceCache = new();
    // Key: (itemId, worldId), Value: recent sales cache entry with up to 5 NQ and 5 HQ prices per world
    private readonly ConcurrentDictionary<(int itemId, int worldId), RecentSalesCacheEntry> _recentSalesCache = new();
    
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disposed;

    private readonly Channel<PriceUpdateWorkItem> _priceUpdateQueue;
    private readonly Task _backgroundWorker;

    private const int WorldDataRefreshHours = 24;
    private const int MarketableItemsRefreshHours = 24;
    private const int ValueSnapshotIntervalMinutes = 15;

    // Spike filtering: only items whose previous sale was at least this many gil are eligible,
    // and a new sale is treated as a spike when it reaches this multiple of the previous sale.
    private const int PriceSpikeMinPreviousGil = 10000;
    private const int PriceSpikeMultiplier = 100;

    // Bulk-sale leniency reaches its configured maximum once the stack size hits this quantity.
    private const int BulkSaleMaxLeniencyQuantity = 100;

    // Inventory price fetching: API allows up to 100 item IDs per request; the auto-fetch path
    // caps its work at a single batch to avoid rate limiting; batches are spaced by a short delay.
    private const int InventoryPriceFetchBatchSize = 100;
    private const int InventoryPriceFetchDelayMs = 100;
    private const int MaxAutoFetchInventoryItems = 100;

    // Inventory sale data older than this is refetched from the API at startup.
    private const int StaleInventoryPriceThresholdMinutes = 5;

    private PriceTrackingSettings Settings => _configService.Config.PriceTracking;
    private InventoryValueSettings ValueSettings => _configService.Config.InventoryValue;

    public UniversalisWorldData? WorldData => _worldData;
    public UniversalisService UniversalisService => _universalisService;
    public ListingsService ListingsService => _listingsService;
    public IReadOnlySet<int>? MarketableItems => _marketableItems;
    public bool IsInitialized => _worldData != null && _marketableItems != null;
    public bool IsSocketConnected => _webSocketService.IsConnected;
    public UniversalisWebSocketService WebSocketService => _webSocketService;

    public event Action<int>? OnPriceDataUpdated;
    public event Action? OnWorldDataLoaded;

    public PriceTrackingService(
        IPluginLog log,
        IFramework framework,
        ConfigurationService configService,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        CurrencyTrackerService currencyTrackerService,
        InventoryCacheService inventoryCacheService,
        ListingsService listingsService,
        ItemDataService itemDataService,
        TimeSeriesCacheService cacheService,
        SalePriceCacheService salePriceCacheService,
        KaleidoscopeDbService dbService,
        CharacterDataCacheService characterDataCache)
    {
        _log = log;
        _framework = framework;
        _configService = configService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _currencyTrackerService = currencyTrackerService;
        _inventoryCacheService = inventoryCacheService;
        _listingsService = listingsService;
        _itemDataService = itemDataService;
        _cacheService = cacheService;
        _salePriceCacheService = salePriceCacheService;
        _dbService = dbService;
        _characterDataCache = characterDataCache;

        // Use bounded channel to prevent unbounded memory growth during high WebSocket activity
        // 10000 items handles bursts; DropOldest discards stale prices (acceptable for market data)
        _priceUpdateQueue = Channel.CreateBounded<PriceUpdateWorkItem>(new BoundedChannelOptions(10000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _backgroundWorker = Task.Factory.StartNew(
            ProcessPriceUpdateQueueAsync,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        ).Unwrap();

        _webSocketService.OnPriceUpdate += OnPriceUpdate;

        _framework.Update += OnFrameworkUpdate;

        LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Service initialized with background thread for price updates");

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] InitializeAsync starting");
            
            if (_disposed)
            {
                LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] InitializeAsync - already disposed, exiting");
                return;
            }
            
            // Pre-populate inventory value cache on background thread
            // This prevents blocking on main thread when InventoryValueTool first draws
            PopulateInventoryValueCache();
            
            // Pre-populate the recent sales cache from the database
            // This ensures outlier detection has reference data immediately
            PopulateRecentSalesCache();
            
            // Fetch world/DC data and marketable items in parallel for faster startup
            var worldDataTask = RefreshWorldDataAsync();
            var marketableItemsTask = RefreshMarketableItemsAsync();
            
            await Task.WhenAll(worldDataTask, marketableItemsTask);

            if (_disposed)
            {
                LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] InitializeAsync - disposed after data fetch, exiting");
                return;
            }
            
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] InitializeAsync - Settings.Enabled = {Settings.Enabled}");
            if (Settings.Enabled)
            {
                LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] InitializeAsync - starting WebSocket");
                await _webSocketService.StartAsync();
                await _webSocketService.SubscribeToAllAsync();
                
                await _listingsService.InitializeAsync(_worldData, _marketableItems);
                
                await FetchStaleInventoryPricesAsync();
            }

            LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Initialization complete");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Populates the recent sales cache from the database at startup.
    /// This ensures outlier detection has reference data immediately.
    /// </summary>
    private void PopulateRecentSalesCache()
    {
        try
        {
            LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Populating recent sales cache from database...");
            
            // Get recent sales from DB (last 5 per item/world/hq type)
            var recentSales = _dbService.GetRecentSalesForCache(
                maxSalesPerType: RecentSalesCacheEntry.MaxSalesPerType);
            
            foreach (var (key, prices) in recentSales)
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
            
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Loaded {_recentSalesCache.Count} item/world combinations into recent sales cache");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Failed to populate recent sales cache: {ex.Message}");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_disposed)
            return;
        
        var now = DateTime.UtcNow;

        // Event-driven value sampling (triggered by price updates or inventory changes)
        var recalcIntervalMs = Settings.ValueRecalcOnEveryUpdate ? 0 : Math.Max(50, Settings.ValueRecalcIntervalMs);
        if (_pendingValueRecalc && 
            (now - _lastEventDrivenValueSample).TotalMilliseconds >= recalcIntervalMs &&
            Settings.Enabled)
        {
            _pendingValueRecalc = false;
            _lastEventDrivenValueSample = now;
            _ = Task.Run(async () =>
            {
                try { await TakeEventDrivenValueSnapshotsAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Event-driven value snapshot failed: {ex.Message}"); }
            });
        }

        if ((now - _lastCleanup).TotalMinutes >= Settings.CleanupIntervalMinutes)
        {
            _lastCleanup = now;
            _ = Task.Run(async () =>
            {
                try { await PerformCleanupAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Cleanup failed: {ex.Message}"); }
            });
        }

        // Periodic value snapshots (fallback for when no events trigger updates)
        if ((now - _lastValueSnapshot).TotalMinutes >= ValueSnapshotIntervalMinutes && Settings.Enabled)
        {
            _lastValueSnapshot = now;
            _ = Task.Run(async () =>
            {
                try { await TakeValueSnapshotsAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Value snapshot failed: {ex.Message}"); }
            });
        }

        if ((now - _lastWorldDataFetch).TotalHours >= WorldDataRefreshHours)
        {
            _lastWorldDataFetch = now;
            _ = Task.Run(async () =>
            {
                try { await RefreshWorldDataAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.Universalis, $"[PriceTracking] World data refresh failed: {ex.Message}"); }
            });
        }

        if ((now - _lastMarketableItemsFetch).TotalHours >= MarketableItemsRefreshHours)
        {
            _lastMarketableItemsFetch = now;
            _ = Task.Run(async () =>
            {
                try { await RefreshMarketableItemsAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.Universalis, $"[PriceTracking] Marketable items refresh failed: {ex.Message}"); }
            });
        }
    }

    private void OnPriceUpdate(PriceFeedEntry entry)
    {
        try
        {
            if (Settings.ExcludedItemIds.Contains(entry.ItemId))
                return;

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
                    LogService.Verbose(LogCategory.PriceTracking, $"[PriceTracking] Ignoring mannequin sale for {itemName} ({entry.ItemId})");
                    return;
                }

                // Check for price spikes (100x or higher than previous sale) only for items with previous sales >= 10k
                // Uses in-memory cache to avoid blocking DB reads on the WebSocket thread
                _lastSalePriceCache.TryGetValue((entry.ItemId, entry.IsHq), out var previousPrice);
                if (previousPrice >= PriceSpikeMinPreviousGil && entry.PricePerUnit >= (long)previousPrice * PriceSpikeMultiplier)
                {
                    var itemName = _itemDataService.GetItemName(entry.ItemId);
                    LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Ignoring price spike for {itemName} ({entry.ItemId}): {entry.PricePerUnit:N0} is 100x+ higher than previous {previousPrice:N0}");
                    return;
                }

                // Check for listing price discrepancy if enabled
                // Uses median/average of lowest 5 listings and last 5 sales for that world as reference
                // Skip the filter if the unit price is below the minimum threshold
                if (Settings.FilterSalesByListingPrice && entry.PricePerUnit >= Settings.SaleFilterMinimumPrice)
                {
                    var listing = _listingsService.GetListing(entry.ItemId, entry.WorldId);
                    
                    // Get listing reference (median or average based on setting)
                    double listingRef = 0;
                    if (listing != null)
                    {
                        listingRef = Settings.UseMedianForReference
                            ? (entry.IsHq ? listing.MedianPriceHq : listing.MedianPriceNq)
                            : (entry.IsHq ? listing.AveragePriceHq : listing.AveragePriceNq);
                    }
                    
                    // Get sale reference and std dev from the cache
                    double saleRef = 0;
                    double saleStdDev = 0;
                    double saleMean = 0;
                    if (_recentSalesCache.TryGetValue((entry.ItemId, entry.WorldId), out var salesCache))
                    {
                        saleRef = Settings.UseMedianForReference
                            ? (entry.IsHq ? salesCache.MedianPriceHq : salesCache.MedianPriceNq)
                            : (entry.IsHq ? salesCache.AveragePriceHq : salesCache.AveragePriceNq);
                        saleStdDev = entry.IsHq ? salesCache.StdDevHq : salesCache.StdDevNq;
                        saleMean = entry.IsHq ? salesCache.AveragePriceHq : salesCache.AveragePriceNq;
                    }
                    
                    // Calculate reference price as average of listing and sale references
                    double referencePrice;
                    if (listingRef > 0 && saleRef > 0)
                        referencePrice = (listingRef + saleRef) / 2.0;
                    else if (listingRef > 0)
                        referencePrice = listingRef;
                    else if (saleRef > 0)
                        referencePrice = saleRef;
                    else
                        referencePrice = 0; // No reference data available
                    
                    if (referencePrice > 0)
                    {
                        bool isOutlier = false;
                        string filterReason = "";
                        
                        if (Settings.UseStdDevFilter && saleStdDev > 0 && saleMean > 0)
                        {
                            // Standard deviation-based filtering
                            var zScore = Math.Abs(entry.PricePerUnit - saleMean) / saleStdDev;
                            if (zScore > Settings.StdDevThreshold)
                            {
                                isOutlier = true;
                                filterReason = $"z-score {zScore:F2} > {Settings.StdDevThreshold:F1}";
                            }
                        }
                        else
                        {
                            // Fixed percentage threshold filtering
                            var ratio = entry.PricePerUnit / referencePrice;
                            var threshold = Settings.SaleDiscrepancyThreshold / 100.0;
                            
                            // Adjust threshold for bulk sales if enabled
                            if (Settings.AdjustForBulkSales && entry.Quantity > 1)
                            {
                                // Linear scaling: more quantity = more lenient, up to max
                                // At BulkSaleMaxLeniencyQuantity items, reach max leniency
                                var quantityFactor = 1.0 + (Math.Min(entry.Quantity, BulkSaleMaxLeniencyQuantity) / (double)BulkSaleMaxLeniencyQuantity) * (Settings.BulkSaleMaxLeniency - 1.0);
                                threshold *= quantityFactor;
                            }
                            
                            var minRatio = 1.0 - threshold;
                            var maxRatio = 1.0 + threshold;
                            if (ratio < minRatio || ratio > maxRatio)
                            {
                                isOutlier = true;
                                var effectiveThreshold = (int)(threshold * 100);
                                filterReason = $"{(ratio * 100 - 100):+0;-0}% from reference (threshold: {effectiveThreshold}%)";
                            }
                        }
                        
                        if (isOutlier)
                        {
                            var itemName = _itemDataService.GetItemName(entry.ItemId);
                            var worldName = _worldData?.GetWorldName(entry.WorldId) ?? entry.WorldId.ToString();
                            var refType = Settings.UseMedianForReference ? "median" : "avg";
                            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Ignoring sale for {itemName} on {worldName}: " +
                                $"price {entry.PricePerUnit:N0} ({filterReason}), ref {referencePrice:N0} ({refType}), qty {entry.Quantity}");
                            return;
                        }
                    }
                }

                var lastSaleNq = entry.IsHq ? 0 : entry.PricePerUnit;
                var lastSaleHq = entry.IsHq ? entry.PricePerUnit : 0;

                // Update SalePriceCacheService immediately for real-time inventory value calculations
                _salePriceCacheService.UpdateGlobalSalePrice(entry.ItemId, entry.IsHq, entry.PricePerUnit);
                _salePriceCacheService.UpdateWorldSalePrice(entry.ItemId, entry.WorldId, entry.IsHq, entry.PricePerUnit);
                _salePriceCacheService.UpdateBatchSalePrice(entry.ItemId, lastSaleNq > 0 ? lastSaleNq : (int?)null, lastSaleHq > 0 ? lastSaleHq : (int?)null);

                // Get existing cached prices to preserve min prices
                var existingNq = 0;
                var existingHq = 0;
                if (_priceCache.TryGetValue(key, out var existing))
                {
                    existingNq = existing.minNq;
                    existingHq = existing.minHq;
                }

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
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Error processing price update: {ex.Message}");
        }
    }

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
                        await Task.Delay(10, _cts.Token);
                    }
                }
                
                if (batch.Count == 0) continue;
                
                // Process the batch - DB writes are done inside SaveSaleRecordsBatch/SaveItemPricesBatch
                // which use transactions to minimize lock time
                try
                {
                    var sales = batch.Where(w => w.IsSale).ToList();
                    var listings = batch.Where(w => !w.IsSale).ToList();
                    
                    if (sales.Count > 0)
                    {
                        var saleRecords = sales.Select(w => (
                            w.ItemId, w.WorldId, w.PricePerUnit, w.Quantity, w.IsHq, w.Total, w.BuyerName
                        )).ToList();
                        _dbService.SaveSaleRecordsBatch(saleRecords);
                        
                        var salePrices = sales.Select(w => (
                            w.ItemId, w.WorldId, w.ExistingMinNq, w.ExistingMinHq, w.LastSaleNq, w.LastSaleHq
                        )).ToList();
                        _dbService.SaveItemPricesBatch(salePrices);
                        
                        foreach (var w in sales)
                        {
                            _lastSalePriceCache[(w.ItemId, w.IsHq)] = w.PricePerUnit;
                            
                            // Update the new recent sales cache (stores up to 5 prices per world per NQ/HQ)
                            var salesCacheKey = (w.ItemId, w.WorldId);
                            _recentSalesCache.AddOrUpdate(
                                salesCacheKey,
                                _ =>
                                {
                                    var entry = new RecentSalesCacheEntry
                                    {
                                        ItemId = w.ItemId,
                                        WorldId = w.WorldId
                                    };
                                    entry.AddSale(w.PricePerUnit, w.IsHq);
                                    return entry;
                                },
                                (_, existing) =>
                                {
                                    existing.AddSale(w.PricePerUnit, w.IsHq);
                                    return existing;
                                });
                            
                            itemsToNotify.Add(w.ItemId);
                        }
                    }
                    
                    if (listings.Count > 0)
                    {
                        var listingPrices = listings.Select(w => (
                            w.ItemId, w.WorldId, w.CachedMinNq, w.CachedMinHq, 0, 0 // No sale prices for listings
                        )).ToList();
                        _dbService.SaveItemPricesBatch(listingPrices);
                        
                        foreach (var w in listings)
                        {
                            itemsToNotify.Add(w.ItemId);
                        }
                    }
                    
                    foreach (var itemId in itemsToNotify)
                    {
                        OnPriceDataUpdated?.Invoke(itemId);
                    }
                    
                    // (Sales are what we use for inventory valuation)
                    if (sales.Count > 0)
                    {
                        _pendingValueRecalc = true;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Verbose(LogCategory.PriceTracking, $"[PriceTracking] Background batch write error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (ObjectDisposedException)
        {
            // Expected during rapid plugin reload - CTS disposed before cancellation signaled
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Background worker crashed: {ex.Message}", ex);
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

        if (_worldData != null)
        {
            var dc = _worldData.GetDataCenterForWorldId(worldId);
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
        if (_worldData == null) return null;

        var mode = GetEffectivePriceMatchMode(characterWorldId);
        return _worldData.GetWorldIdsForPriceMatchMode(characterWorldId, mode);
    }

    /// <summary>
    /// Refreshes the cached world/DC data from Universalis.
    /// Falls back to static data if the API is unavailable.
    /// </summary>
    public async Task RefreshWorldDataAsync()
    {
        if (_disposed) return;
        
        const int maxRetries = 3;
        const int retryDelayMs = 2000;
        
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Fetching world data from Universalis (attempt {attempt}/{maxRetries})");

                // Fetch worlds and data centers in parallel with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var worldsTask = _universalisService.GetWorldsAsync(cts.Token);
                var dataCentersTask = _universalisService.GetDataCentersAsync(cts.Token);
                
                await Task.WhenAll(worldsTask, dataCentersTask);
                
                var worlds = await worldsTask;
                var dataCenters = await dataCentersTask;

                if (worlds != null && dataCenters != null && worlds.Count > 0 && dataCenters.Count > 0)
                {
                    _worldData = new UniversalisWorldData
                    {
                        Worlds = worlds,
                        DataCenters = dataCenters,
                        LastUpdated = DateTime.UtcNow
                    };
                    _lastWorldDataFetch = DateTime.UtcNow;

                    LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Loaded {worlds.Count} worlds, {dataCenters.Count} data centers from API");
                    
                    OnWorldDataLoaded?.Invoke();
                    return; // Success, exit retry loop
                }
                
                LogService.Warning(LogCategory.PriceTracking, $"[PriceTracking] API returned empty data (attempt {attempt}/{maxRetries})");
            }
            catch (OperationCanceledException)
            {
                LogService.Warning(LogCategory.PriceTracking, $"[PriceTracking] Timeout fetching world data (attempt {attempt}/{maxRetries})");
            }
            catch (Exception ex)
            {
                LogService.Warning(LogCategory.PriceTracking, $"[PriceTracking] Failed to fetch world data (attempt {attempt}/{maxRetries}): {ex.Message}");
            }
            
            // Wait before retrying (except on last attempt)
            if (attempt < maxRetries)
            {
                await Task.Delay(retryDelayMs);
            }
        }
        
        UseFallbackWorldData();
    }
    
    /// <summary>
    /// Uses static fallback world data when the Universalis API is unavailable.
    /// </summary>
    private void UseFallbackWorldData()
    {
        // Only use fallback if we don't already have valid data
        if (_worldData != null && _worldData.Worlds.Count > 0)
        {
            LogService.Warning(LogCategory.PriceTracking, "[PriceTracking] API unavailable, keeping existing world data");
            return;
        }
        
        LogService.Warning(LogCategory.PriceTracking, "[PriceTracking] Using fallback world data - API unavailable after all retries");
        
        _worldData = FallbackWorldData.CreateFallback();
        
        LogService.Info(LogCategory.PriceTracking, $"[PriceTracking] Loaded fallback data: {_worldData.Worlds.Count} worlds, {_worldData.DataCenters.Count} data centers");
        
        // Still notify subscribers so UI can render
        OnWorldDataLoaded?.Invoke();
    }

    /// <summary>
    /// Refreshes the list of marketable items from Universalis.
    /// </summary>
    public async Task RefreshMarketableItemsAsync()
    {
        if (_disposed) return;
        
        try
        {
            LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Fetching marketable items from Universalis");

            var items = await _universalisService.GetMarketableItemsAsync();

            if (items != null)
            {
                _marketableItems = items.ToHashSet();
                _lastMarketableItemsFetch = DateTime.UtcNow;

                LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Loaded {items.Count} marketable items");
            }
        }
        catch (Exception ex)
        {
            LogService.Warning(LogCategory.PriceTracking, $"[PriceTracking] Failed to fetch marketable items: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current price for an item.
    /// First checks cache, then database, optionally fetches from API.
    /// </summary>
    public async Task<(int MinPriceNq, int MinPriceHq)?> GetItemPriceAsync(int itemId, int? worldId = null, bool fetchIfMissing = true)
    {
        if (_marketableItems != null && !_marketableItems.Contains(itemId))
        {
            return null;
        }

        if (worldId.HasValue)
        {
            var key = (itemId, worldId.Value);
            if (_priceCache.TryGetValue(key, out var cached))
            {
                return (cached.minNq, cached.minHq);
            }
        }

        var dbResult = worldId.HasValue 
            ? _dbService.GetItemPrice(itemId, worldId.Value)
            : null;
        
        if (dbResult.HasValue)
        {
            return (dbResult.Value.MinPriceNq, dbResult.Value.MinPriceHq);
        }

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

            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Fetching price for item {itemId} from API");

            var data = await _universalisService.GetAggregatedDataAsync(scope, (uint)itemId);
            if (data?.Results == null || data.Results.Count == 0)
            {
                return null;
            }

            var result = data.Results[0];
            var (nqPrice, hqPrice, lastSaleNq, lastSaleHq) = result.ExtractPrices();

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
                    _dbService.SaveItemPrice(itemId, wid.Value, nqPrice, hqPrice, 
                        lastSaleNq: lastSaleNq, lastSaleHq: lastSaleHq);
                }
            }

            return (nqPrice, hqPrice);
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] API fetch failed for item {itemId}: {ex.Message}");
            return null;
        }
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

        foreach (var cache in caches)
        {
            if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                continue;

            gilValue += cache.Gil;

            foreach (var item in cache.Items)
            {
                if (_marketableItems != null && !_marketableItems.Contains((int)item.ItemId))
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
        if (_worldData == null) return null;

        // Find the player cache entry (not retainer) to get the world
        var playerCache = caches.FirstOrDefault(c => c.SourceType == Models.Inventory.InventorySourceType.Player);
        if (playerCache?.World == null) return null;

        return _worldData.GetWorldId(playerCache.World);
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
                _dbService.SaveInventoryValueHistory(charId, total, gil, item, contributions);
                
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
    private async Task TakeEventDrivenValueSnapshotsAsync()
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
    private void PopulateInventoryValueCache()
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
    /// Performs cleanup of old price data based on retention settings.
    /// </summary>
    /// <summary>
    /// Manually triggers price data retention cleanup.
    /// Returns the number of records deleted.
    /// </summary>
    public async Task<int> TriggerCleanupAsync()
    {
        if (_disposed) return 0;

        var settings = Settings;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        LogService.Debug(LogCategory.PriceTracking,
            $"[PriceTracking] Manual cleanup started — policy: {settings.RetentionType}, " +
            (settings.RetentionType == PriceRetentionType.ByTime
                ? $"retaining last {settings.RetentionDays} day(s)"
                : $"target size {settings.RetentionSizeMb} MB"));

        try
        {
            var sizeBefore = _dbService.GetPriceDataSize();
            LogService.Debug(LogCategory.PriceTracking,
                $"[PriceTracking] Pre-cleanup estimated data size: {sizeBefore / 1024.0 / 1024.0:F2} MB");

            int deleted;
            switch (settings.RetentionType)
            {
                case PriceRetentionType.ByTime:
                    LogService.Debug(LogCategory.PriceTracking,
                        $"[PriceTracking] Deleting records older than {DateTime.UtcNow.AddDays(-settings.RetentionDays):yyyy-MM-dd HH:mm} UTC...");
                    deleted = _dbService.CleanupOldPriceData(settings.RetentionDays);
                    break;

                case PriceRetentionType.BySize:
                    var maxBytes = settings.RetentionSizeMb * 1024L * 1024L;
                    LogService.Debug(LogCategory.PriceTracking,
                        $"[PriceTracking] Deleting oldest records to fit under {settings.RetentionSizeMb} MB ({maxBytes:N0} bytes)...");
                    deleted = _dbService.CleanupPriceDataBySize(maxBytes);
                    break;

                default:
                    LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Manual cleanup skipped — unknown retention type");
                    return 0;
            }

            sw.Stop();
            var sizeAfter = _dbService.GetPriceDataSize();

            if (deleted > 0)
            {
                LogService.Debug(LogCategory.PriceTracking,
                    $"[PriceTracking] Manual cleanup completed in {sw.ElapsedMilliseconds} ms — " +
                    $"deleted {deleted} record(s), size {sizeBefore / 1024.0 / 1024.0:F2} MB → {sizeAfter / 1024.0 / 1024.0:F2} MB " +
                    $"(freed ~{(sizeBefore - sizeAfter) / 1024.0 / 1024.0:F2} MB)");
            }
            else
            {
                LogService.Debug(LogCategory.PriceTracking,
                    $"[PriceTracking] Manual cleanup completed in {sw.ElapsedMilliseconds} ms — no records needed cleanup " +
                    $"(current size: {sizeAfter / 1024.0 / 1024.0:F2} MB)");
            }

            return deleted;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogService.Error(LogCategory.PriceTracking,
                $"[PriceTracking] Manual cleanup failed after {sw.ElapsedMilliseconds} ms: {ex.Message}", ex);
            throw;
        }
    }

    private async Task PerformCleanupAsync()
    {
        if (_disposed) return;
        
        try
        {
            var settings = Settings;

            switch (settings.RetentionType)
            {
                case PriceRetentionType.ByTime:
                    var deleted = _dbService.CleanupOldPriceData(settings.RetentionDays);
                    if (deleted > 0)
                    {
                        LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Cleaned up {deleted} old records (time-based)");
                    }
                    break;

                case PriceRetentionType.BySize:
                    var maxBytes = settings.RetentionSizeMb * 1024L * 1024L;
                    var deletedBySize = _dbService.CleanupPriceDataBySize(maxBytes);
                    if (deletedBySize > 0)
                    {
                        LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Cleaned up {deletedBySize} records (size-based)");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Cleanup error: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Fetches prices for all items in the player's inventories.
    /// Capped at a single batch (<see cref="MaxAutoFetchInventoryItems"/> items) to avoid rate limiting.
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
                .Take(MaxAutoFetchInventoryItems)
                .ToList();

            if (itemIds.Count == 0) return;

            var scope = _universalisService.GetConfiguredScope();
            if (string.IsNullOrEmpty(scope)) return;

            var wid = _worldData?.GetWorldId(scope);
            if (!wid.HasValue) return;

            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Fetching prices for {itemIds.Count} inventory items");

            await FetchAndSaveInventoryPricesAsync(itemIds, scope, wid.Value);

            LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Finished fetching inventory prices");
        }
        catch (Exception ex)
        {
            LogService.Warning(LogCategory.PriceTracking, $"[PriceTracking] Error fetching inventory prices: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches prices for inventory items that have stale or missing sale data.
    /// Only fetches items where the last update is older than the stale threshold.
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
                LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] No inventory items to check for stale prices");
                return;
            }

            // Get items with stale or missing sale data
            var staleThreshold = TimeSpan.FromMinutes(StaleInventoryPriceThresholdMinutes);
            var staleItemIds = _dbService.GetStaleItemIds(allItemIds, staleThreshold);

            if (staleItemIds.Count == 0)
            {
                LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] All inventory items have fresh price data");
                return;
            }

            if (string.IsNullOrEmpty(scope))
            {
                LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] No scope configured, skipping stale price fetch");
                return;
            }

            var wid = _worldData?.GetWorldId(scope);
            if (!wid.HasValue)
            {
                LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] No world ID for scope, skipping stale price fetch");
                return;
            }

            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Fetching prices for {staleItemIds.Count} stale inventory items");

            await FetchAndSaveInventoryPricesAsync(staleItemIds.ToList(), scope, wid.Value);

            LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Finished fetching stale inventory prices ({staleItemIds.Count} items)");
        }
        catch (Exception ex)
        {
            LogService.Warning(LogCategory.PriceTracking, $"[PriceTracking] Error fetching stale inventory prices: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches aggregated prices for the given item IDs from the API and persists them in batches.
    /// Item IDs are chunked to respect the API's per-request limit and spaced by a short delay to
    /// avoid rate limiting. Shared by the auto-fetch and stale-refresh paths.
    /// </summary>
    private async Task FetchAndSaveInventoryPricesAsync(IReadOnlyList<int> itemIds, string scope, int worldId)
    {
        foreach (var batch in itemIds.Chunk(InventoryPriceFetchBatchSize))
        {
            if (_disposed) break;

            var data = await _universalisService.GetAggregatedDataAsync(scope, batch.Select(i => (uint)i));
            if (data?.Results == null) continue;

            var pricesToSave = data.Results.Select(result =>
            {
                var (nqPrice, hqPrice, lastSaleNq, lastSaleHq) = result.ExtractPrices();
                return (result.ItemId, worldId, nqPrice, hqPrice, lastSaleNq, lastSaleHq);
            }).ToList();

            // Batch save to reduce lock contention
            _dbService.SaveItemPricesBatch(pricesToSave);

            await Task.Delay(InventoryPriceFetchDelayMs);
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

            foreach (var cache in caches)
            {
                if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                    continue;

                foreach (var item in cache.Items)
                {
                    if (_marketableItems != null && !_marketableItems.Contains((int)item.ItemId))
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

    /// <summary>
    /// Enables or disables price tracking.
    /// </summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        Settings.Enabled = enabled;
        _configService.MarkDirty();

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

        LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Reconnecting WebSocket to apply channel subscription changes...");
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
            LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Resetting all Universalis data...");

            _priceCache.Clear();

            var result = _dbService.ClearAllPriceData();

            if (result)
            {
                LogService.Info(LogCategory.PriceTracking, "[PriceTracking] All Universalis data has been reset");
            }
            else
            {
                LogService.Warning(LogCategory.PriceTracking, "[PriceTracking] Failed to reset Universalis data");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Error resetting data: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        
        try { _cts.Cancel(); }
        catch (Exception) { /* Ignore */ }

        _priceUpdateQueue.Writer.TryComplete();

        try { _backgroundWorker.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* Ignore timeout */ }
        
        _framework.Update -= OnFrameworkUpdate;
        _webSocketService.OnPriceUpdate -= OnPriceUpdate;
        
        try { _cts.Dispose(); }
        catch (Exception) { /* Ignore */ }
    }
}

/// <summary>
/// Work item representing a price update to be persisted to the database.
/// </summary>
/// <param name="IsSale">Whether this is a sale event (true) or listing event (false).</param>
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
