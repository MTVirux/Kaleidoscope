using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Universalis;
using OtterGui.Services;

namespace Kaleidoscope.Services.Universalis;

/// <summary>
/// Owns the cached Universalis world/data-center map and the marketable-item set, including their
/// API refresh (with retry and static fallback). Acts as the single source of truth for this data;
/// <see cref="PriceTrackingService"/> and <see cref="InventoryValuationService"/> read through it.
/// </summary>
public sealed class WorldDataProvider : IService, IDisposable
{
    private readonly IPluginLog _log;
    private readonly UniversalisService _universalisService;

    private UniversalisWorldData? _worldData;
    private HashSet<int>? _marketableItems;
    private DateTime _lastWorldDataFetch = DateTime.MinValue;
    private DateTime _lastMarketableItemsFetch = DateTime.MinValue;

    private volatile bool _disposed;

    /// <summary>The cached world/data-center map, or null until first loaded.</summary>
    public UniversalisWorldData? WorldData => _worldData;

    /// <summary>The cached set of marketable item IDs, or null until first loaded.</summary>
    public IReadOnlySet<int>? MarketableItems => _marketableItems;

    /// <summary>True once both world data and marketable items have been loaded.</summary>
    public bool IsInitialized => _worldData != null && _marketableItems != null;

    /// <summary>UTC time of the last successful (or scheduled) world-data fetch.</summary>
    public DateTime LastWorldDataFetch => _lastWorldDataFetch;

    /// <summary>UTC time of the last successful (or scheduled) marketable-items fetch.</summary>
    public DateTime LastMarketableItemsFetch => _lastMarketableItemsFetch;

    /// <summary>Raised whenever world data is (re)loaded, including via fallback.</summary>
    public event Action? OnWorldDataLoaded;

    public WorldDataProvider(IPluginLog log, UniversalisService universalisService)
    {
        _log = log;
        _universalisService = universalisService;
    }

    /// <summary>
    /// Records that a world-data refresh has been kicked off so scheduled refreshes are not
    /// re-triggered while one is already in flight.
    /// </summary>
    public void MarkWorldDataRefreshScheduled(DateTime now) => _lastWorldDataFetch = now;

    /// <summary>
    /// Records that a marketable-items refresh has been kicked off so scheduled refreshes are not
    /// re-triggered while one is already in flight.
    /// </summary>
    public void MarkMarketableItemsRefreshScheduled(DateTime now) => _lastMarketableItemsFetch = now;

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

    public void Dispose()
    {
        _disposed = true;
    }
}
