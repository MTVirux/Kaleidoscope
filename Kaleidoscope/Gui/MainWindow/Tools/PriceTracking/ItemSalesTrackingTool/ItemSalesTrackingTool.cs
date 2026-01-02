using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Services;
using MTGui.Common;
using MTGui.Graph;
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
public partial class ItemSalesTrackingTool : ToolComponent
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
        
        _graphWidget.BindSettings(
            _instanceSettings,
            () => { _seriesDataDirty = true; NotifyToolSettingsChanged(); },
            "Graph Settings");
        
        RegisterSettingsProvider(_graphWidget);

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
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    private void DrawItemSelector()
    {
        ImGui.TextUnformatted("Track Items:");
        ImGui.SameLine();

        if (_itemCombo.DrawMultiSelect(300))
        {
        }

        if (_isLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Loading...");
        }

        if (!string.IsNullOrEmpty(_errorMessage))
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), _errorMessage);
        }
    }

    private void DrawSalesGraph()
    {
        if (_seriesDataDirty || _cachedSeriesData == null)
        {
            BuildSeriesData();
            _seriesDataDirty = false;
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

    public override void Dispose()
    {
        _webSocketService.OnPriceUpdate -= OnPriceUpdate;
        _itemCombo.MultiSelectionChanged -= OnItemSelectionChanged;
        _itemCombo.Dispose();
        base.Dispose();
    }
}
