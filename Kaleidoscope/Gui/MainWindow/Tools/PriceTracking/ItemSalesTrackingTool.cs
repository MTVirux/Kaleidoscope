using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using MTGui.Common;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that tracks and graphs sales data for one or more items from Universalis.
/// Receives real-time updates via WebSocket for items within the configured world scope.
/// </summary>
public class ItemSalesTrackingTool : ToolComponent
{
    public override string ToolName => "Item Sales Tracking";
    protected override bool HasToolSettings => true;

    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly ConfigurationService _configService;
    private readonly ItemDataService _itemDataService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly SalePriceCacheService _salePriceCacheService;
    private readonly MTItemComboDropdown _itemCombo;
    private readonly MTGraphWidget _graphWidget;

    // World selection for filtering sales scope
    private WorldSelectionWidget? _worldSelectionWidget;
    private bool _worldSelectionWidgetInitialized;

    // Instance settings persisted with the layout
    private readonly ItemSalesTrackingSettings _instanceSettings;

    // Sales data cache per item
    // Key: ItemId, Value: List of (timestamp, price)
    private readonly Dictionary<uint, List<(DateTime Timestamp, float Price)>> _salesDataCache = new();
    private DateTime _lastFetchTime = DateTime.MinValue;
    private bool _isLoading;
    private string? _errorMessage;
    private HashSet<uint> _loadedItemIds = new();

    // For graph rendering
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? _cachedSeriesData;
    private bool _seriesDataDirty = true;

    private ItemSalesTrackingSettings Settings => _instanceSettings;

    public ItemSalesTrackingTool(
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        CurrencyTrackerService currencyTrackerService,
        SalePriceCacheService salePriceCacheService,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        FavoritesService favoritesService)
    {
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _priceTrackingService = priceTrackingService;
        _configService = configService;
        _itemDataService = itemDataService;
        _currencyTrackerService = currencyTrackerService;
        _salePriceCacheService = salePriceCacheService;

        _instanceSettings = new ItemSalesTrackingSettings();

        // Create multi-select item combo (marketable only)
        _itemCombo = new MTItemComboDropdown(
            textureProvider,
            dataManager,
            favoritesService,
            priceTrackingService,
            "ItemSalesTracking",
            marketableOnly: true,
            configService: configService,
            trackedDataRegistry: currencyTrackerService.Registry,
            excludeCurrencies: true,
            multiSelect: true);

        _itemCombo.MultiSelectionChanged += OnItemSelectionChanged;

        // Create graph widget
        var graphConfig = new MTGraphConfig
        {
            PlotId = "ItemSalesTrackingGraph",
            NoDataText = "Select items to track sales data.",
            ShowLegend = true,
            LegendPosition = MTLegendPosition.InsideTopLeft,
            GraphType = MTGraphType.Line,
            ShowCrosshair = true,
            ShowGridLines = true,
            AutoScrollEnabled = true,
            AutoScrollTimeValue = 24,
            AutoScrollTimeUnit = MTTimeUnit.Hours,
            SimulateRealTimeUpdates = false
        };
        _graphWidget = new MTGraphWidget(graphConfig);
        
        // Bind graph widget to settings for persistence
        _graphWidget.BindSettings(
            _instanceSettings,
            () => { _seriesDataDirty = true; NotifyToolSettingsChanged(); },
            "Graph Settings");
        
        RegisterSettingsProvider(_graphWidget);

        // Subscribe to WebSocket updates for real-time sales
        _webSocketService.OnPriceUpdate += OnPriceUpdate;

        Title = "Item Sales Tracking";
        Size = new Vector2(500, 350);
    }

    public override void RenderToolContent()
    {
        try
        {
            DrawItemSelector();
            ImGui.Separator();

            using (ProfilerService.BeginStaticChildScope("DrawSalesGraph"))
            {
                DrawSalesGraph();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[ItemSalesTrackingTool] Draw error: {ex.Message}");
        }
    }

    private void DrawItemSelector()
    {
        ImGui.TextUnformatted("Track Items:");
        ImGui.SameLine();

        // Multi-select item picker
        if (_itemCombo.DrawMultiSelect(300))
        {
            // Selection changed - handled via event
        }

        // Show loading indicator
        if (_isLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Loading...");
        }

        // Show error if any
        if (!string.IsNullOrEmpty(_errorMessage))
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), _errorMessage);
        }
    }

    private void DrawSalesGraph()
    {
        // Build series data if dirty
        if (_seriesDataDirty || _cachedSeriesData == null)
        {
            BuildSeriesData();
            _seriesDataDirty = false;
        }

        // Get available size for graph
        var availableSize = ImGui.GetContentRegionAvail();
        if (availableSize.X < 50 || availableSize.Y < 50)
            return;

        if (_cachedSeriesData == null || _cachedSeriesData.Count == 0)
        {
            if (_itemCombo.SelectedItemIds.Count == 0)
            {
                ImGui.TextDisabled("Select items above to track their sales.");
            }
            else if (_isLoading)
            {
                ImGui.TextDisabled("Loading sales data...");
            }
            else
            {
                ImGui.TextDisabled("No sales data available for selected items.");
            }
            return;
        }

        // Render the graph
        using (ProfilerService.BeginStaticChildScope("MTGraph.RenderMultipleSeries"))
        {
            _graphWidget.RenderMultipleSeries(_cachedSeriesData);
        }
    }

    private void BuildSeriesData()
    {
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        var colorIndex = 0;

        // Get outlier filter settings
        var filterOutliers = Settings.FilterOutliers;
        var priceSettings = _configService.Config.PriceTracking;
        var threshold = priceSettings.SaleDiscrepancyThreshold / 100.0;
        var minRatio = 1.0 - threshold;
        var maxRatio = 1.0 + threshold;
        var listingsService = _priceTrackingService.ListingsService;

        foreach (var itemId in _itemCombo.SelectedItemIds)
        {
            if (!_salesDataCache.TryGetValue(itemId, out var salesData) || salesData.Count == 0)
                continue;

            var itemName = _itemDataService.GetItemName(itemId) ?? $"Item {itemId}";
            var color = GetSeriesColor(colorIndex++);

            // Apply outlier filter if enabled
            IEnumerable<(DateTime Timestamp, float Price)> filteredData = salesData;
            if (filterOutliers)
            {
                // Get reference prices for this item (using NQ as baseline since we don't track HQ in the cache)
                var listing = listingsService.GetLowestListingAcrossWorlds((int)itemId);
                var listingPrice = listing?.MinPriceNq ?? 0;
                var recentSalePrice = _salePriceCacheService.GetMostRecentSalePrice((int)itemId, isHq: false);

                // Calculate reference price
                double referencePrice = 0;
                if (listingPrice > 0 && recentSalePrice > 0)
                    referencePrice = (listingPrice + recentSalePrice) / 2.0;
                else if (listingPrice > 0)
                    referencePrice = listingPrice;
                else if (recentSalePrice > 0)
                    referencePrice = recentSalePrice;

                // Only filter if we have a reference price
                if (referencePrice > 0)
                {
                    filteredData = salesData.Where(sale =>
                    {
                        var ratio = sale.Price / referencePrice;
                        return ratio >= minRatio && ratio <= maxRatio;
                    });
                }
            }

            // Convert to the format expected by the graph
            var samples = filteredData
                .OrderBy(s => s.Timestamp)
                .Select(s => (s.Timestamp, s.Price))
                .ToList();

            if (samples.Count > 0)
            {
                series.Add((itemName, samples, color));
            }
        }

        _cachedSeriesData = series;

        // Update Y-axis bounds based on data
        if (series.Count > 0)
        {
            var allPrices = series.SelectMany(s => s.samples.Select(p => p.value)).ToList();
            if (allPrices.Count > 0)
            {
                var minPrice = allPrices.Min();
                var maxPrice = allPrices.Max();
                var padding = (maxPrice - minPrice) * 0.1f;
                _graphWidget.UpdateBounds(
                    Math.Max(0, minPrice - padding),
                    maxPrice + padding);
            }
        }
    }

    private static Vector4 GetSeriesColor(int index)
    {
        // Color palette for different items
        var colors = new[]
        {
            new Vector4(0.4f, 0.8f, 0.4f, 1f), // Green
            new Vector4(0.4f, 0.6f, 1f, 1f),   // Blue
            new Vector4(1f, 0.6f, 0.4f, 1f),   // Orange
            new Vector4(0.8f, 0.4f, 0.8f, 1f), // Purple
            new Vector4(1f, 0.8f, 0.4f, 1f),   // Yellow
            new Vector4(0.4f, 0.8f, 0.8f, 1f), // Cyan
            new Vector4(1f, 0.4f, 0.6f, 1f),   // Pink
            new Vector4(0.6f, 0.6f, 0.6f, 1f), // Gray
        };
        return colors[index % colors.Length];
    }

    private void OnItemSelectionChanged(IReadOnlySet<uint> selectedIds)
    {
        // Find newly selected items that need fetching
        var newItems = selectedIds.Except(_loadedItemIds).ToList();

        if (newItems.Count > 0)
        {
            _ = FetchHistoryForItemsAsync(newItems);
        }

        // Remove cached data for deselected items (optional - keep for performance)
        var removedItems = _loadedItemIds.Except(selectedIds).ToList();
        foreach (var itemId in removedItems)
        {
            _salesDataCache.Remove(itemId);
            _loadedItemIds.Remove(itemId);
        }

        _seriesDataDirty = true;
    }

    private async Task FetchAllHistoryAsync()
    {
        var selectedIds = _itemCombo.SelectedItemIds.ToList();
        if (selectedIds.Count == 0) return;

        _loadedItemIds.Clear();
        _salesDataCache.Clear();

        await FetchHistoryForItemsAsync(selectedIds);
    }

    private async Task FetchHistoryForItemsAsync(IEnumerable<uint> itemIds)
    {
        _isLoading = true;
        _errorMessage = null;

        try
        {
            var scopes = GetQueryScopes();
            if (scopes.Count == 0)
            {
                _errorMessage = "Cannot determine query scope - check settings.";
                return;
            }

            var itemIdList = itemIds.ToList();
            
            // Batch items into chunks of 100 (Universalis API limit)
            const int batchSize = 100;
            var batches = new List<List<uint>>();
            for (var i = 0; i < itemIdList.Count; i += batchSize)
            {
                batches.Add(itemIdList.Skip(i).Take(batchSize).ToList());
            }

            foreach (var batch in batches)
            {
                // Initialize sales data accumulators for this batch
                var batchSalesData = batch.ToDictionary(id => id, _ => new List<(DateTime, float)>());

                // Query each scope and combine results
                foreach (var scope in scopes)
                {
                    try
                    {
                        var historyBatch = await _universalisService.GetHistoryBatchAsync(scope, batch, Settings.MaxHistoryEntries);
                        if (historyBatch != null)
                        {
                            foreach (var kvp in historyBatch)
                            {
                                var itemId = kvp.Key;
                                var history = kvp.Value;
                                
                                if (history?.Entries != null && history.Entries.Count > 0)
                                {
                                    var filteredEntries = FilterEntriesByWorldScope(history.Entries);
                                    var salesData = filteredEntries
                                        .Select(e => (e.SaleDateTime, (float)e.PricePerUnit));
                                    
                                    if (batchSalesData.TryGetValue(itemId, out var existingData))
                                    {
                                        existingData.AddRange(salesData);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug($"[ItemSalesTrackingTool] Error fetching batch history for scope {scope}: {ex.Message}");
                    }
                }

                // Process and cache results for this batch
                foreach (var kvp in batchSalesData)
                {
                    var itemId = kvp.Key;
                    var allSalesData = kvp.Value;

                    // Sort by timestamp and deduplicate (in case of overlapping data)
                    _salesDataCache[itemId] = allSalesData
                        .GroupBy(s => s.Item1) // Deduplicate by timestamp
                        .Select(g => g.First())
                        .OrderBy(s => s.Item1)
                        .ToList();

                    _loadedItemIds.Add(itemId);
                }
            }

            _lastFetchTime = DateTime.UtcNow;
            _seriesDataDirty = true;
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error fetching data: {ex.Message}";
            LogService.Error($"[ItemSalesTrackingTool] FetchHistoryForItemsAsync error: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private List<HistorySale> FilterEntriesByWorldScope(List<HistorySale> entries)
    {
        // If scope mode is All, return all entries
        if (Settings.ScopeMode == WorldSelectionMode.Worlds && Settings.SelectedWorldIds.Count == 0)
            return entries;

        var effectiveWorldIds = GetEffectiveWorldIds();
        if (effectiveWorldIds.Count == 0)
            return entries;

        return entries.Where(e => e.WorldId.HasValue && effectiveWorldIds.Contains(e.WorldId.Value)).ToList();
    }

    private HashSet<int> GetEffectiveWorldIds()
    {
        if (_worldSelectionWidget != null)
            return _worldSelectionWidget.GetEffectiveWorldIds();

        // Fall back to settings
        return Settings.ScopeMode switch
        {
            WorldSelectionMode.Worlds => Settings.SelectedWorldIds,
            WorldSelectionMode.DataCenters => ResolveDataCentersToWorldIds(Settings.SelectedDataCenters),
            WorldSelectionMode.Regions => ResolveRegionsToWorldIds(Settings.SelectedRegions),
            _ => new HashSet<int>()
        };
    }

    private HashSet<int> ResolveDataCentersToWorldIds(HashSet<string> dcNames)
    {
        var result = new HashSet<int>();
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null) return result;

        foreach (var dcName in dcNames)
        {
            var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
            if (dc?.Worlds != null)
            {
                foreach (var wid in dc.Worlds)
                    result.Add(wid);
            }
        }
        return result;
    }

    private HashSet<int> ResolveRegionsToWorldIds(HashSet<string> regions)
    {
        var result = new HashSet<int>();
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null) return result;

        foreach (var region in regions)
        {
            foreach (var dc in worldData.GetDataCentersForRegion(region))
            {
                if (dc.Worlds != null)
                {
                    foreach (var wid in dc.Worlds)
                        result.Add(wid);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Returns all scopes that need to be queried to cover all selected worlds/DCs/regions.
    /// This ensures multiple regions are each queried separately.
    /// </summary>
    private List<string> GetQueryScopes()
    {
        var worldData = _priceTrackingService.WorldData;
        var result = new List<string>();
        
        if (worldData == null)
        {
            var fallback = _universalisService.GetConfiguredScope();
            if (fallback != null)
                result.Add(fallback);
            return result;
        }

        switch (Settings.ScopeMode)
        {
            case WorldSelectionMode.Regions when Settings.SelectedRegions.Count > 0:
                // Query each selected region separately
                result.AddRange(Settings.SelectedRegions);
                break;

            case WorldSelectionMode.DataCenters when Settings.SelectedDataCenters.Count > 0:
                // Group DCs by region and query at the region level for efficiency
                var dcsByRegion = new Dictionary<string, List<string>>();
                foreach (var dcName in Settings.SelectedDataCenters)
                {
                    var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                    var region = dc?.Region ?? "Unknown";
                    if (!dcsByRegion.ContainsKey(region))
                        dcsByRegion[region] = new List<string>();
                    dcsByRegion[region].Add(dcName);
                }
                
                // If we have all DCs in a region, query the region; otherwise query individual DCs
                foreach (var kvp in dcsByRegion)
                {
                    var region = kvp.Key;
                    var dcsInRegion = kvp.Value;
                    var allDcsInRegion = worldData.DataCenters
                        .Where(dc => dc.Region == region)
                        .Select(dc => dc.Name)
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToList();
                    
                    // Check if we selected all DCs in this region
                    if (allDcsInRegion.All(dc => dcsInRegion.Contains(dc!)) && allDcsInRegion.Count > 0)
                    {
                        result.Add(region);
                    }
                    else
                    {
                        // Query each DC individually
                        result.AddRange(dcsInRegion);
                    }
                }
                break;

            case WorldSelectionMode.Worlds when Settings.SelectedWorldIds.Count > 0:
                // Group worlds by DC and DC by region for optimal querying
                var worldsByDc = new Dictionary<string, List<int>>();
                var dcToRegion = new Dictionary<string, string>();
                
                foreach (var worldId in Settings.SelectedWorldIds)
                {
                    var dc = worldData.GetDataCenterForWorldId(worldId);
                    if (dc != null && !string.IsNullOrEmpty(dc.Name))
                    {
                        if (!worldsByDc.ContainsKey(dc.Name))
                            worldsByDc[dc.Name] = new List<int>();
                        worldsByDc[dc.Name].Add(worldId);
                        if (!string.IsNullOrEmpty(dc.Region))
                            dcToRegion[dc.Name] = dc.Region;
                    }
                }
                
                // Group DCs by region
                var dcsByRegionFromWorlds = new Dictionary<string, List<string>>();
                foreach (var kvp in worldsByDc)
                {
                    var dcName = kvp.Key;
                    var region = dcToRegion.GetValueOrDefault(dcName, "Unknown");
                    if (!dcsByRegionFromWorlds.ContainsKey(region))
                        dcsByRegionFromWorlds[region] = new List<string>();
                    dcsByRegionFromWorlds[region].Add(dcName);
                }
                
                // For each region, check if we can query at region level
                foreach (var regionKvp in dcsByRegionFromWorlds)
                {
                    var region = regionKvp.Key;
                    var dcsInThisRegion = regionKvp.Value;
                    
                    // Check if all worlds in all DCs of this region are selected
                    var allDcsInRegion = worldData.DataCenters
                        .Where(dc => dc.Region == region)
                        .ToList();
                    
                    var allWorldsInRegionSelected = true;
                    foreach (var dc in allDcsInRegion)
                    {
                        if (dc.Worlds != null)
                        {
                            foreach (var wid in dc.Worlds)
                            {
                                if (!Settings.SelectedWorldIds.Contains((int)wid))
                                {
                                    allWorldsInRegionSelected = false;
                                    break;
                                }
                            }
                        }
                        if (!allWorldsInRegionSelected) break;
                    }
                    
                    if (allWorldsInRegionSelected && allDcsInRegion.Count > 0)
                    {
                        result.Add(region);
                    }
                    else
                    {
                        // Check each DC if we can query at DC level
                        foreach (var dcName in dcsInThisRegion)
                        {
                            var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                            if (dc?.Worlds != null)
                            {
                                var allWorldsInDcSelected = dc.Worlds.All(wid => Settings.SelectedWorldIds.Contains((int)wid));
                                if (allWorldsInDcSelected)
                                {
                                    result.Add(dcName);
                                }
                                else
                                {
                                    // Query individual worlds
                                    foreach (var wid in worldsByDc[dcName])
                                    {
                                        var worldName = worldData.GetWorldName(wid);
                                        if (!string.IsNullOrEmpty(worldName))
                                            result.Add(worldName);
                                    }
                                }
                            }
                        }
                    }
                }
                break;

            default:
                var fallbackScope = _universalisService.GetConfiguredScope();
                if (fallbackScope != null)
                    result.Add(fallbackScope);
                break;
        }
        
        // Ensure we have at least one scope
        if (result.Count == 0)
        {
            var fallbackScope = _universalisService.GetConfiguredScope();
            if (fallbackScope != null)
                result.Add(fallbackScope);
        }
        
        return result.Distinct().ToList();
    }

    private void OnPriceUpdate(PriceFeedEntry entry)
    {
        // Only process sales events
        if (entry.EventType != "Sale")
            return;

        // Check if this item is being tracked
        if (!_itemCombo.SelectedItemIds.Contains((uint)entry.ItemId))
            return;

        // Check if this world is in our scope
        var effectiveWorldIds = GetEffectiveWorldIds();
        if (effectiveWorldIds.Count > 0 && !effectiveWorldIds.Contains(entry.WorldId))
            return;

        // Add the sale to our cache
        var itemId = (uint)entry.ItemId;
        if (!_salesDataCache.TryGetValue(itemId, out var salesData))
        {
            salesData = new List<(DateTime, float)>();
            _salesDataCache[itemId] = salesData;
        }

        salesData.Add((entry.ReceivedAt, entry.PricePerUnit));

        // Trim to max entries
        while (salesData.Count > Settings.MaxHistoryEntries)
        {
            salesData.RemoveAt(0);
        }

        // Mark for rebuild
        _seriesDataDirty = true;
    }

    protected override void DrawToolSettings()
    {
        ImGui.TextUnformatted("Sales Data Settings");
        ImGui.Spacing();

        // Max history entries
        var maxEntries = Settings.MaxHistoryEntries;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Max History Entries", ref maxEntries))
        {
            Settings.MaxHistoryEntries = Math.Clamp(maxEntries, 10, 1000);
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Maximum number of sales to display per item (10-1000)");
        }

        // Outlier filter
        var filterOutliers = Settings.FilterOutliers;
        if (ImGui.Checkbox("Filter Outliers", ref filterOutliers))
        {
            Settings.FilterOutliers = filterOutliers;
            _seriesDataDirty = true;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            var priceSettings = _configService.Config.PriceTracking;
            var threshold = priceSettings.SaleDiscrepancyThreshold;
            var refType = priceSettings.UseMedianForReference ? "median" : "average";
            var filterType = priceSettings.UseStdDevFilter ? "std dev" : $"{threshold}%";
            ImGui.SetTooltip($"Filter out sales with prices far from expected values.\nIgnore sales outside {filterType} threshold.\nReference = {refType}(lowest 5 listings, last 5 sales) per world.\nConfigure thresholds in Settings > Universalis.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // World scope selection
        ImGui.TextUnformatted("Query Scope");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Filter sales data by world/datacenter/region.\nAlso determines which WebSocket sales updates are received.");
        }

        DrawWorldSelectionWidget();
    }

    private void DrawWorldSelectionWidget()
    {
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null)
        {
            ImGui.TextDisabled("World data not loaded...");
            return;
        }

        // Initialize widget if needed
        if (_worldSelectionWidget == null)
        {
            _worldSelectionWidget = new WorldSelectionWidget(worldData, "ItemSalesTrackingScope");
            _worldSelectionWidget.Width = 280f;
        }

        if (!_worldSelectionWidgetInitialized)
        {
            _worldSelectionWidget.InitializeFrom(
                Settings.SelectedRegions,
                Settings.SelectedDataCenters,
                Settings.SelectedWorldIds);
            _worldSelectionWidget.Mode = Settings.ScopeMode;
            _worldSelectionWidgetInitialized = true;
        }

        // Draw the widget
        if (_worldSelectionWidget.Draw("Market Scope##SalesTrackingScope"))
        {
            // Sync back to settings
            Settings.SelectedRegions.Clear();
            foreach (var r in _worldSelectionWidget.SelectedRegions)
                Settings.SelectedRegions.Add(r);

            Settings.SelectedDataCenters.Clear();
            foreach (var dc in _worldSelectionWidget.SelectedDataCenters)
                Settings.SelectedDataCenters.Add(dc);

            Settings.SelectedWorldIds.Clear();
            foreach (var w in _worldSelectionWidget.SelectedWorldIds)
                Settings.SelectedWorldIds.Add(w);

            Settings.ScopeMode = _worldSelectionWidget.Mode;

            NotifyToolSettingsChanged();

            // Re-fetch data with new scope
            _ = FetchAllHistoryAsync();
        }
    }

    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["SelectedItemIds"] = _itemCombo.SelectedItemIds.ToList(),
            ["MaxHistoryEntries"] = Settings.MaxHistoryEntries,
            ["FilterOutliers"] = Settings.FilterOutliers,
            ["ScopeMode"] = (int)Settings.ScopeMode,
            ["SelectedRegions"] = Settings.SelectedRegions.ToList(),
            ["SelectedDataCenters"] = Settings.SelectedDataCenters.ToList(),
            ["SelectedWorldIds"] = Settings.SelectedWorldIds.ToList()
        };
    }

    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;

        var selectedItemIds = GetSetting<List<uint>>(settings, "SelectedItemIds");
        if (selectedItemIds != null && selectedItemIds.Count > 0)
        {
            _itemCombo.SetMultiSelection(selectedItemIds);
        }

        Settings.MaxHistoryEntries = GetSetting(settings, "MaxHistoryEntries", 100);
        Settings.FilterOutliers = GetSetting(settings, "FilterOutliers", true);
        Settings.ScopeMode = (WorldSelectionMode)GetSetting(settings, "ScopeMode", 0);

        var regions = GetSetting<List<string>>(settings, "SelectedRegions");
        if (regions != null)
        {
            Settings.SelectedRegions.Clear();
            foreach (var r in regions)
                Settings.SelectedRegions.Add(r);
        }

        var dataCenters = GetSetting<List<string>>(settings, "SelectedDataCenters");
        if (dataCenters != null)
        {
            Settings.SelectedDataCenters.Clear();
            foreach (var dc in dataCenters)
                Settings.SelectedDataCenters.Add(dc);
        }

        var worldIds = GetSetting<List<int>>(settings, "SelectedWorldIds");
        if (worldIds != null)
        {
            Settings.SelectedWorldIds.Clear();
            foreach (var w in worldIds)
                Settings.SelectedWorldIds.Add(w);
        }

        _worldSelectionWidgetInitialized = false;
        
        // Trigger refresh of sales data for any selected items
        var selectedIds = _itemCombo.SelectedItemIds.ToList();
        if (selectedIds.Count > 0)
        {
            _ = FetchHistoryForItemsAsync(selectedIds);
        }
    }

    public override void Dispose()
    {
        _webSocketService.OnPriceUpdate -= OnPriceUpdate;
        _itemCombo.MultiSelectionChanged -= OnItemSelectionChanged;
        _itemCombo.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Instance settings for ItemSalesTrackingTool.
/// Implements IGraphWidgetSettings for automatic graph widget binding.
/// </summary>
public class ItemSalesTrackingSettings : Kaleidoscope.Models.IGraphWidgetSettings
{
    // Tool-specific settings
    public int MaxHistoryEntries { get; set; } = 100;
    public bool FilterOutliers { get; set; } = true;
    public WorldSelectionMode ScopeMode { get; set; } = WorldSelectionMode.Worlds;
    public HashSet<string> SelectedRegions { get; set; } = new();
    public HashSet<string> SelectedDataCenters { get; set; } = new();
    public HashSet<int> SelectedWorldIds { get; set; } = new();
    
    // === IGraphWidgetSettings implementation ===
    
    // Color mode for series
    public Kaleidoscope.Models.GraphColorMode ColorMode { get; set; } = Kaleidoscope.Models.GraphColorMode.PreferredItemColors;
    
    // Legend settings
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public bool LegendCollapsed { get; set; } = false;
    public MTLegendPosition LegendPosition { get; set; } = MTLegendPosition.InsideTopLeft;
    
    // Graph type (default to Line for sales data)
    public MTGraphType GraphType { get; set; } = MTGraphType.Line;
    
    // Display settings
    public bool ShowXAxisTimestamps { get; set; } = true;
    public bool ShowCrosshair { get; set; } = true;
    public bool ShowGridLines { get; set; } = true;
    public bool ShowCurrentPriceLine { get; set; } = true;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    // Auto-scroll settings (enabled by default for real-time sales)
    public bool AutoScrollEnabled { get; set; } = true;
    public int AutoScrollTimeValue { get; set; } = 24;
    public MTTimeUnit AutoScrollTimeUnit { get; set; } = MTTimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    
    // Time range settings
    public int TimeRangeValue { get; set; } = 7;
    public MTTimeUnit TimeRangeUnit { get; set; } = MTTimeUnit.Days;
    
    // Number format settings
    public NumberFormatConfig NumberFormat { get; set; } = new();
}
