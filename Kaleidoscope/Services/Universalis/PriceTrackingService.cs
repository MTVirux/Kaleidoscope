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
/// Orchestrates item price tracking via the Universalis API and WebSocket.
/// Owns the live sale/listing caches and DB persistence, and delegates world/marketable-item data
/// to <see cref="WorldDataProvider"/>, inventory valuation to <see cref="InventoryValuationService"/>,
/// and outlier decisions to <see cref="SaleOutlierFilter"/>.
/// </summary>
public sealed class PriceTrackingService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly ConfigurationService _configService;
    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly ListingsService _listingsService;
    private readonly ItemDataService _itemDataService;
    private readonly SalePriceCacheService _salePriceCacheService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly WorldDataProvider _worldDataProvider;
    private readonly InventoryValuationService _inventoryValuationService;

    private DateTime _lastCleanup = DateTime.MinValue;
    private DateTime _lastValueSnapshot = DateTime.MinValue;

    private DateTime _lastEventDrivenValueSample = DateTime.MinValue;
    private volatile bool _pendingValueRecalc = false;

    private readonly ConcurrentDictionary<(int itemId, int worldId), (int minNq, int minHq, DateTime updated)> _priceCache = new();

    // In-memory cache for recent sale prices (used for spike detection without DB reads)
    // Key: (itemId, isHq), Value: last sale price (global, for spike detection)
    private readonly ConcurrentDictionary<(int itemId, bool isHq), int> _lastSalePriceCache = new();
    // Key: (itemId, worldId), Value: recent sales cache entry with up to 5 NQ and 5 HQ prices per world
    private readonly ConcurrentDictionary<(int itemId, int worldId), RecentSalesCacheEntry> _recentSalesCache = new();

    // (minNq, minHq, lastSaleNq, lastSaleHq) pending DB persistence. Live reads never touch the
    // DB — this batches upserts so the websocket firehose doesn't rewrite item_prices constantly.
    private readonly ConcurrentDictionary<(int ItemId, int WorldId), (int MinNq, int MinHq, int LastSaleNq, int LastSaleHq)> _dirtyItemPrices = new();
    private DateTime _lastItemPriceFlush = DateTime.UtcNow;
    private const int ItemPriceFlushIntervalMinutes = 5;

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

    public UniversalisWorldData? WorldData => _worldDataProvider.WorldData;
    public UniversalisService UniversalisService => _universalisService;
    public ListingsService ListingsService => _listingsService;
    public IReadOnlySet<int>? MarketableItems => _worldDataProvider.MarketableItems;
    public bool IsInitialized => _worldDataProvider.IsInitialized;
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
        InventoryCacheService inventoryCacheService,
        ListingsService listingsService,
        ItemDataService itemDataService,
        SalePriceCacheService salePriceCacheService,
        KaleidoscopeDbService dbService,
        WorldDataProvider worldDataProvider,
        InventoryValuationService inventoryValuationService)
    {
        _log = log;
        _framework = framework;
        _configService = configService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _inventoryCacheService = inventoryCacheService;
        _listingsService = listingsService;
        _itemDataService = itemDataService;
        _salePriceCacheService = salePriceCacheService;
        _dbService = dbService;
        _worldDataProvider = worldDataProvider;
        _inventoryValuationService = inventoryValuationService;

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
        _worldDataProvider.OnWorldDataLoaded += OnProviderWorldDataLoaded;

        _framework.Update += OnFrameworkUpdate;

        LogService.Debug(LogCategory.PriceTracking, "[PriceTracking] Service initialized with background thread for price updates");

        _ = InitializeAsync();
    }

    private void OnProviderWorldDataLoaded() => OnWorldDataLoaded?.Invoke();

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
            _inventoryValuationService.PopulateInventoryValueCache();

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

                await _listingsService.InitializeAsync(_worldDataProvider.WorldData, _worldDataProvider.MarketableItems);

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
                try { await _inventoryValuationService.TakeEventDrivenValueSnapshotsAsync(); }
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

        if ((now - _lastItemPriceFlush).TotalMinutes >= ItemPriceFlushIntervalMinutes)
        {
            _lastItemPriceFlush = now;
            _ = Task.Run(() =>
            {
                try { FlushDirtyItemPrices(); }
                catch (Exception ex) { LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Item price flush failed: {ex.Message}"); }
            });
        }

        // Periodic value snapshots (fallback for when no events trigger updates)
        if ((now - _lastValueSnapshot).TotalMinutes >= ValueSnapshotIntervalMinutes && Settings.Enabled)
        {
            _lastValueSnapshot = now;
            _ = Task.Run(async () =>
            {
                try { await _inventoryValuationService.TakeValueSnapshotsAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.PriceTracking, $"[PriceTracking] Value snapshot failed: {ex.Message}"); }
            });
        }

        if ((now - _worldDataProvider.LastWorldDataFetch).TotalHours >= WorldDataRefreshHours)
        {
            _worldDataProvider.MarkWorldDataRefreshScheduled(now);
            _ = Task.Run(async () =>
            {
                try { await RefreshWorldDataAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.Universalis, $"[PriceTracking] World data refresh failed: {ex.Message}"); }
            });
        }

        if ((now - _worldDataProvider.LastMarketableItemsFetch).TotalHours >= MarketableItemsRefreshHours)
        {
            _worldDataProvider.MarkMarketableItemsRefreshScheduled(now);
            _ = Task.Run(async () =>
            {
                try { await RefreshMarketableItemsAsync(); }
                catch (Exception ex) { LogService.Error(LogCategory.Universalis, $"[PriceTracking] Marketable items refresh failed: {ex.Message}"); }
            });
        }
    }

    private void MarkItemPriceDirty(int itemId, int worldId, int minNq, int minHq, int lastSaleNq, int lastSaleHq)
    {
        _dirtyItemPrices.AddOrUpdate(
            (itemId, worldId),
            _ => (minNq, minHq, lastSaleNq, lastSaleHq),
            (_, prev) => (
                minNq,
                minHq,
                // Listing events carry no sale info (zeros) — keep the last known sale prices
                // instead of clobbering them, which the old per-event upsert used to do.
                lastSaleNq > 0 ? lastSaleNq : prev.LastSaleNq,
                lastSaleHq > 0 ? lastSaleHq : prev.LastSaleHq));
    }

    private void FlushDirtyItemPrices()
    {
        if (_dirtyItemPrices.IsEmpty) return;

        var keys = _dirtyItemPrices.Keys.ToList();
        var batch = new List<(int, int, int, int, int, int)>(keys.Count);
        foreach (var key in keys)
        {
            if (_dirtyItemPrices.TryRemove(key, out var v))
                batch.Add((key.ItemId, key.WorldId, v.MinNq, v.MinHq, v.LastSaleNq, v.LastSaleHq));
        }
        if (batch.Count > 0)
            _dbService.SaveItemPricesBatch(batch);
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
                    _recentSalesCache.TryGetValue((entry.ItemId, entry.WorldId), out var salesCache);

                    if (SaleOutlierFilter.IsOutlier(
                            entry.PricePerUnit, entry.Quantity, entry.IsHq,
                            listing, salesCache, Settings, BulkSaleMaxLeniencyQuantity,
                            out var referencePrice, out var filterReason))
                    {
                        var itemName = _itemDataService.GetItemName(entry.ItemId);
                        var worldName = _worldDataProvider.WorldData?.GetWorldName(entry.WorldId) ?? entry.WorldId.ToString();
                        var refType = Settings.UseMedianForReference ? "median" : "avg";
                        LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Ignoring sale for {itemName} on {worldName}: " +
                            $"price {entry.PricePerUnit:N0} ({filterReason}), ref {referencePrice:N0} ({refType}), qty {entry.Quantity}");
                        return;
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
                // Listing event - only "Listing Added" carries a live price we can merge into the
                // min-price cache. "Listing Removed" (and any other non-sale event) reports a price
                // that is no longer available, so applying it would pollute the cache with stale lows.
                if (entry.EventType != "Listing Added")
                    return;

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

                        foreach (var w in sales)
                            MarkItemPriceDirty(w.ItemId, w.WorldId, w.ExistingMinNq, w.ExistingMinHq, w.LastSaleNq, w.LastSaleHq);

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
                        foreach (var w in listings)
                        {
                            // Listings carry no sale info, so pass zeros — MarkItemPriceDirty preserves prior sale prices.
                            MarkItemPriceDirty(w.ItemId, w.WorldId, w.CachedMinNq, w.CachedMinHq, 0, 0);
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
        var worldData = _worldDataProvider.WorldData;

        switch (settings.ScopeMode)
        {
            case PriceTrackingScopeMode.All:
                return true;

            case PriceTrackingScopeMode.ByWorld:
                return settings.SelectedWorldIds.Contains(worldId);

            case PriceTrackingScopeMode.ByDataCenter:
                if (worldData == null) return true;
                var worldName = worldData.GetWorldName(worldId);
                if (worldName == null) return true;
                foreach (var dcName in settings.SelectedDataCenters)
                {
                    var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                    if (dc?.Worlds?.Contains(worldId) == true) return true;
                }
                return false;

            case PriceTrackingScopeMode.ByRegion:
                if (worldData == null) return true;
                foreach (var regionName in settings.SelectedRegions)
                {
                    foreach (var dc in worldData.GetDataCentersForRegion(regionName))
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
        => _inventoryValuationService.GetEffectivePriceMatchMode(worldId);

    /// <summary>
    /// Refreshes the cached world/DC data from Universalis.
    /// Falls back to static data if the API is unavailable.
    /// </summary>
    public Task RefreshWorldDataAsync()
    {
        if (_disposed) return Task.CompletedTask;
        return _worldDataProvider.RefreshWorldDataAsync();
    }

    /// <summary>
    /// Refreshes the list of marketable items from Universalis.
    /// </summary>
    public Task RefreshMarketableItemsAsync()
    {
        if (_disposed) return Task.CompletedTask;
        return _worldDataProvider.RefreshMarketableItemsAsync();
    }

    /// <summary>
    /// Gets the current price for an item.
    /// First checks cache, then database, optionally fetches from API.
    /// </summary>
    public async Task<(int MinPriceNq, int MinPriceHq)?> GetItemPriceAsync(int itemId, int? worldId = null, bool fetchIfMissing = true)
    {
        var marketableItems = _worldDataProvider.MarketableItems;
        if (marketableItems != null && !marketableItems.Contains(itemId))
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
            var worldData = _worldDataProvider.WorldData;
            if (worldData != null)
            {
                var wid = worldData.GetWorldId(scope);
                if (wid.HasValue)
                {
                    MarkItemPriceDirty(itemId, wid.Value, nqPrice, hqPrice, lastSaleNq, lastSaleHq);
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
    public Task<(long TotalValue, long GilValue, long ItemValue, List<(int ItemId, long Quantity, int UnitPrice)> ItemContributions)> CalculateInventoryValueAsync(ulong characterId, bool includeRetainers = true)
        => _inventoryValuationService.CalculateInventoryValueAsync(characterId, includeRetainers);

    /// <summary>
    /// Manually triggers price data retention cleanup.
    /// Returns the number of records deleted.
    /// </summary>
    public async Task<int> TriggerCleanupAsync()
    {
        if (_disposed) return 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        LogService.Debug(LogCategory.PriceTracking,
            $"[PriceTracking] Manual cleanup started — retaining last {Settings.RetentionDays} day(s) of value history");

        try
        {
            var deleted = _dbService.CleanupOldPriceData(Settings.RetentionDays);
            sw.Stop();
            LogService.Debug(LogCategory.PriceTracking,
                $"[PriceTracking] Manual cleanup completed in {sw.ElapsedMilliseconds} ms — deleted {deleted} record(s)");
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
            var deleted = _dbService.CleanupOldPriceData(Settings.RetentionDays);
            if (deleted > 0)
            {
                LogService.Debug(LogCategory.PriceTracking, $"[PriceTracking] Cleaned up {deleted} old value history records");
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
            var marketableItems = _worldDataProvider.MarketableItems;
            var allCaches = _inventoryCacheService.GetAllInventories();
            var itemIds = allCaches
                .SelectMany(c => c.Items.Select(i => (int)i.ItemId))
                .Distinct()
                .Where(id => marketableItems?.Contains(id) ?? true)
                .Take(MaxAutoFetchInventoryItems)
                .ToList();

            if (itemIds.Count == 0) return;

            var scope = _universalisService.GetConfiguredScope();
            if (string.IsNullOrEmpty(scope)) return;

            var wid = _worldDataProvider.WorldData?.GetWorldId(scope);
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
                var marketableItems = _worldDataProvider.MarketableItems;
                var allCaches = _inventoryCacheService.GetAllInventories();
                allItemIds = allCaches
                    .SelectMany(c => c.Items.Select(i => (int)i.ItemId))
                    .Distinct()
                    .Where(id => marketableItems?.Contains(id) ?? true)
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

            var wid = _worldDataProvider.WorldData?.GetWorldId(scope);
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

            foreach (var result in data.Results)
            {
                var (nqPrice, hqPrice, lastSaleNq, lastSaleHq) = result.ExtractPrices();
                MarkItemPriceDirty(result.ItemId, worldId, nqPrice, hqPrice, lastSaleNq, lastSaleHq);
            }

            await Task.Delay(InventoryPriceFetchDelayMs);
        }
    }

    /// <summary>
    /// Gets the top items by value for a character or all characters.
    /// When a specific character is specified, uses their world's price match mode.
    /// When all characters are requested, uses global prices.
    /// </summary>
    public Task<List<(int ItemId, long Quantity, long Value)>> GetTopItemsByValueAsync(
        ulong? characterId = null,
        int maxItems = 100,
        bool includeRetainers = true)
        => _inventoryValuationService.GetTopItemsByValueAsync(characterId, maxItems, includeRetainers);

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

        // Persist any pending price upserts while _dbService is still usable; best effort on shutdown.
        try { FlushDirtyItemPrices(); }
        catch { /* best effort on shutdown */ }

        _framework.Update -= OnFrameworkUpdate;
        _webSocketService.OnPriceUpdate -= OnPriceUpdate;
        _worldDataProvider.OnWorldDataLoaded -= OnProviderWorldDataLoaded;

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
