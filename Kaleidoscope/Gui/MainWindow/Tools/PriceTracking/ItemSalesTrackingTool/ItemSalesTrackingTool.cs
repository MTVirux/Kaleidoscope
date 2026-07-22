using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets.Common;
using Kaleidoscope.Gui.Widgets.Graph;
using Kaleidoscope.Services.Universalis;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that tracks and graphs sales data for one or more items from Universalis.
/// Receives real-time updates via WebSocket for items within the configured world scope.
/// 
/// Split into partial classes:
/// - ItemSalesTrackingTool.cs (this file): Main rendering and graph logic
/// - ItemSalesTrackingTool.DataFetching.cs: Data fetching and world scope resolution
/// - ItemSalesTrackingTool.Settings.cs: Settings UI and import/export logic
/// </summary>
[ToolType("ItemSalesTracking", "Item Sales Tracking", "Universalis",
    "Track and graph sales data for multiple items with real-time WebSocket updates",
    RequiredServices = new[] { typeof(UniversalisWebSocketService), typeof(PriceTrackingService), typeof(ItemDataService), typeof(SalePriceCacheService) })]
public sealed partial class ItemSalesTrackingTool : ToolComponent
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
    private readonly ItemComboDropdown _itemCombo;
    private readonly GraphWidget _graphWidget;

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
    // Set when settings/data change so the cached graph series are rebuilt on the next draw.
    private bool _seriesDataDirty = true;
    // Throttles dirty-driven rebuilds: a burst of WebSocket sales coalesces into at most one
    // rebuild per interval instead of one per frame.
    private DateTime _lastSeriesBuildTime = DateTime.MinValue;
    private const double SeriesRebuildIntervalMs = 500;
    // Sale price cache version consumed by the last series build; a mismatch means a background
    // price refresh landed and the outlier reference price may have changed.
    private long _lastSalePriceVersion = -1;

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

        _instanceSettings = new ItemSalesTrackingSettings
        {
            NumberFormat = configService.Config.DefaultGraphNumberFormat.Clone()
        };

        _itemCombo = new ItemComboDropdown(
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

        var graphConfig = new GraphConfig
        {
            PlotId = "ItemSalesTrackingGraph",
            NoDataText = "Select items to track sales data.",
            ShowLegend = true,
            LegendPosition = LegendPosition.InsideTopLeft,
            GraphType = GraphType.Line,
            ShowCrosshair = true,
            ShowGridLines = true,
            AutoScrollEnabled = true,
            AutoScrollTimeValue = 24,
            AutoScrollTimeUnit = TimeUnit.Hours,
            SimulateRealTimeUpdates = false,
            Style = configService.Config.GraphStyle
        };
        _graphWidget = new GraphWidget(graphConfig);
        
        _graphWidget.BindSettings(
            _instanceSettings,
            () => { _seriesDataDirty = true; NotifyToolSettingsChanged(); },
            "Graph Settings",
            showLegendSettings: true,
            hideCharacterColorMode: true);
        
        RegisterSettingsProvider(_graphWidget);

        _webSocketService.OnPriceUpdate += OnPriceUpdate;

        Title = "Item Sales Tracking";
        Size = new Vector2(500, 350);
    }

    public override void RenderToolContent()
    {
        try
        {
            if (Settings.ShowActionButtons)
            {
                DrawItemSelector();
                ImGui.Separator();
            }

            using (ProfilerService.BeginStaticChildScope("DrawSalesGraph"))
            {
                DrawSalesGraph();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(UiColors.ErrorText, $"Error: {ex.Message}");
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    private void DrawItemSelector()
    {
        // Calculate widths based on available space
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var style = ImGui.GetStyle();
        
        // Fixed width elements
        var labelWidth = ImGui.CalcTextSize("Track Items:").X + style.ItemSpacing.X;
        var buttonWidth = ImGui.CalcTextSize("↻").X + style.FramePadding.X * 2 + style.ItemSpacing.X;
        var loadingWidth = _isLoading ? ImGui.CalcTextSize("Loading...").X + style.ItemSpacing.X : 0;
        
        // Reserve space for fixed elements and spacing between combos
        var remainingWidth = availableWidth - labelWidth - buttonWidth - loadingWidth - style.ItemSpacing.X;
        
        // Split remaining width: 50% each for item combo and scope selector
        var comboWidth = Math.Max(100f, (remainingWidth - style.ItemSpacing.X) * 0.5f);
        
        ImGui.TextUnformatted("Track Items:");
        ImGui.SameLine();

        _itemCombo.DrawMultiSelect(comboWidth);

        var hasSelectedItems = _itemCombo.SelectedItemIds.Count > 0;
        // World scope selector inline
        ImGui.SameLine();
        DrawInlineWorldSelector(comboWidth);

        // Refresh button to manually fetch data from Universalis
        ImGui.SameLine();

        if (!hasSelectedItems)
            ImGui.BeginDisabled();
        
        if (ImGui.SmallButton(_isLoading ? "..." : "↻"))
        {
            if (!_isLoading && hasSelectedItems)
            {
                _ = FetchAllHistoryAsync();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            var scopeDescription = GetCurrentScopeDescription();
            ImGui.SetTooltip($"Refresh sales data from Universalis\nScope: {scopeDescription}");
        }
        
        if (!hasSelectedItems)
            ImGui.EndDisabled();

        if (_isLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Loading...");
        }

        if (!string.IsNullOrEmpty(_errorMessage))
        {
            ImGui.TextColored(UiColors.ErrorMessage, _errorMessage);
        }
    }

    private void DrawInlineWorldSelector(float width)
    {
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null)
        {
            // Show placeholder with proper width while world data loads
            ImGui.SetNextItemWidth(width);
            ImGui.BeginDisabled();
            if (ImGui.BeginCombo("##SalesTrackingScopePlaceholder", "Loading..."))
            {
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            return;
        }

        EnsureWorldSelectionWidgetInitialized(worldData);

        // Use the calculated width for inline display
        _worldSelectionWidget!.Width = width;
        
        if (_worldSelectionWidget.Draw("##SalesTrackingScopeInline"))
        {
            SyncWorldSelectionToSettings();
            NotifyToolSettingsChanged();
            _ = FetchAllHistoryAsync();
        }
    }

    private void EnsureWorldSelectionWidgetInitialized(UniversalisWorldData worldData)
    {
        if (_worldSelectionWidget == null)
        {
            _worldSelectionWidget = new WorldSelectionWidget(worldData, "ItemSalesTrackingScope");
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
    }

    private void DrawSalesGraph()
    {
        // Read before the build so a refresh landing mid-build still re-dirties the series.
        var salePriceVersion = _salePriceCacheService.Version;
        if (salePriceVersion != _lastSalePriceVersion)
            _seriesDataDirty = true;

        if (_cachedSeriesData == null)
        {
            BuildSeriesData();
            _seriesDataDirty = false;
            _lastSeriesBuildTime = DateTime.UtcNow;
            _lastSalePriceVersion = salePriceVersion;
        }
        else if (_seriesDataDirty
                 && (DateTime.UtcNow - _lastSeriesBuildTime).TotalMilliseconds >= SeriesRebuildIntervalMs)
        {
            BuildSeriesData();
            _seriesDataDirty = false;
            _lastSeriesBuildTime = DateTime.UtcNow;
            _lastSalePriceVersion = salePriceVersion;
        }

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

        using (ProfilerService.BeginStaticChildScope("Graph.RenderMultipleSeries"))
        {
            _graphWidget.RenderMultipleSeries(_cachedSeriesData);
        }
    }

    private void BuildSeriesData()
    {
        var selectedCount = _itemCombo.SelectedItemIds.Count;
        var cacheCount = _salesDataCache.Count;
        LogDebug($"BuildSeriesData: Building series for {selectedCount} selected items, {cacheCount} items in cache");
        
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        var colorIndex = 0;

        // Get outlier filter settings
        var filterOutliers = Settings.FilterOutliers;
        var priceSettings = _configService.Config.PriceTracking;
        var threshold = priceSettings.SaleDiscrepancyThreshold / 100.0;
        var listingsService = _priceTrackingService.ListingsService;

        // World scope for outlier reference pricing. Resolved once so the per-item reference
        // lookup can hit the cache per world instead of scanning the entire listings cache.
        var outlierScopeWorldIds = filterOutliers ? GetEffectiveWorldIds() : null;

        foreach (var itemId in _itemCombo.SelectedItemIds)
        {
            if (!_salesDataCache.TryGetValue(itemId, out var salesData))
            {
                LogDebug($"BuildSeriesData: Item {itemId} not found in cache");
                continue;
            }
            
            if (salesData.Count == 0)
            {
                LogDebug($"BuildSeriesData: Item {itemId} has 0 data points in cache");
                continue;
            }
            
            LogDebug($"BuildSeriesData: Item {itemId} has {salesData.Count} data points in cache");

            var itemName = _itemDataService.GetItemName(itemId) ?? $"Item {itemId}";
            var color = GetEffectiveSeriesColor(itemId, colorIndex++);

            // Apply outlier filter if enabled
            IEnumerable<(DateTime Timestamp, float Price)> filteredData = salesData;
            if (filterOutliers)
            {
                // Get reference prices for this item (using NQ as baseline since we don't track HQ in the cache)
                var listingPrice = GetLowestNqListingPrice(listingsService, (int)itemId, outlierScopeWorldIds);
                var recentSalePrice = _salePriceCacheService.GetMostRecentSalePrice((int)itemId, isHq: false);

                var referencePrice = SaleOutlierFilter.ComputeReferencePrice(listingPrice, recentSalePrice);

                // Only filter if we have a reference price
                if (referencePrice > 0)
                {
                    filteredData = salesData.Where(sale =>
                        !SaleOutlierFilter.IsRatioOutlier(sale.Price, referencePrice, threshold, out _));
                }
                else
                {
                    LogDebug($"BuildSeriesData: Item {itemId} has no reference price for outlier filtering, skipping filter");
                }
            }

            // Convert to the format expected by the graph
            var samples = filteredData
                .OrderBy(s => s.Timestamp)
                .Select(s => (s.Timestamp, s.Price))
                .ToList();
            
            if (filterOutliers && salesData.Count != samples.Count)
            {
                LogDebug($"BuildSeriesData: Item {itemId} outlier filter: {salesData.Count} -> {samples.Count} samples");
            }

            if (samples.Count > 0)
            {
                series.Add((itemName, samples, color));
                LogDebug($"BuildSeriesData: Added series '{itemName}' with {samples.Count} samples");
            }
            else
            {
                LogDebug($"BuildSeriesData: Item {itemId} ('{itemName}') has 0 samples after processing, not adding to series");
            }
        }

        _cachedSeriesData = series;
        LogDebug($"BuildSeriesData: Final series count: {series.Count}");

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

    /// <summary>
    /// Lowest NQ listing price used as the outlier-filter reference. When a world scope is
    /// active, this does per-world O(1) cache lookups across just those worlds instead of the
    /// full-cache scan in <see cref="ListingsService.GetLowestListingAcrossWorlds"/>.
    /// </summary>
    private static int GetLowestNqListingPrice(ListingsService listingsService, int itemId, HashSet<int>? worldIds)
    {
        if (worldIds == null || worldIds.Count == 0)
            return listingsService.GetLowestListingAcrossWorlds(itemId)?.MinPriceNq ?? 0;

        var lowest = int.MaxValue;
        foreach (var worldId in worldIds)
        {
            var price = listingsService.GetListing(itemId, worldId)?.MinPriceNq ?? 0;
            if (price > 0 && price < lowest)
                lowest = price;
        }

        return lowest == int.MaxValue ? 0 : lowest;
    }

    /// <summary>
    /// Gets the effective color for a series based on ColorMode setting.
    /// </summary>
    private Vector4 GetEffectiveSeriesColor(uint itemId, int seriesIndex)
    {
        // Check ColorMode for preferred item colors
        if (Settings.ColorMode == Models.GraphColorMode.PreferredItemColors)
        {
            var preferredColor = GetPreferredItemColor(itemId);
            if (preferredColor.HasValue)
                return preferredColor.Value;
        }
        
        // Fallback to default color rotation
        return GetDefaultSeriesColor(seriesIndex);
    }
    
    /// <summary>
    /// Gets the preferred color for an item from configuration.
    /// </summary>
    private Vector4? GetPreferredItemColor(uint itemId)
    {
        if (_configService.Config.GameItemColors.TryGetValue(itemId, out var colorUint))
            return Gui.Common.ColorUtils.UintToVector4(colorUint);
        return null;
    }

    private static Vector4 GetDefaultSeriesColor(int index)
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

    public override void Dispose()
    {
        _webSocketService.OnPriceUpdate -= OnPriceUpdate;
        _itemCombo.MultiSelectionChanged -= OnItemSelectionChanged;
        _itemCombo.Dispose();
        base.Dispose();
    }
}
