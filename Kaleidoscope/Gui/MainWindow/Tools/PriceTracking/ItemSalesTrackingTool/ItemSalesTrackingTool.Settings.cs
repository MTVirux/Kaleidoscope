using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using MTGui.Common;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// ItemSalesTrackingTool partial class containing settings UI and import/export logic.
/// </summary>
public partial class ItemSalesTrackingTool
{
    protected override void DrawToolSettings()
    {
        ImGui.TextUnformatted("Sales Data Settings");
        ImGui.Spacing();

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

        if (_worldSelectionWidget.Draw("Market Scope##SalesTrackingScope"))
        {
            SyncWorldSelectionToSettings();
            NotifyToolSettingsChanged();
            _ = FetchAllHistoryAsync();
        }
    }

    private void SyncWorldSelectionToSettings()
    {
        Settings.SelectedRegions.Clear();
        foreach (var r in _worldSelectionWidget!.SelectedRegions)
            Settings.SelectedRegions.Add(r);

        Settings.SelectedDataCenters.Clear();
        foreach (var dc in _worldSelectionWidget.SelectedDataCenters)
            Settings.SelectedDataCenters.Add(dc);

        Settings.SelectedWorldIds.Clear();
        foreach (var w in _worldSelectionWidget.SelectedWorldIds)
            Settings.SelectedWorldIds.Add(w);

        Settings.ScopeMode = _worldSelectionWidget.Mode;
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
        
        var selectedIds = _itemCombo.SelectedItemIds.ToList();
        if (selectedIds.Count > 0)
        {
            _ = FetchHistoryForItemsAsync(selectedIds);
        }
    }
}

/// <summary>
/// Instance settings for ItemSalesTrackingTool.
/// Implements IGraphWidgetSettings for automatic graph widget binding.
/// </summary>
public class ItemSalesTrackingSettings : IGraphWidgetSettings
{
    // Tool-specific settings
    public int MaxHistoryEntries { get; set; } = 100;
    public bool FilterOutliers { get; set; } = true;
    public WorldSelectionMode ScopeMode { get; set; } = WorldSelectionMode.Worlds;
    public HashSet<string> SelectedRegions { get; set; } = new();
    public HashSet<string> SelectedDataCenters { get; set; } = new();
    public HashSet<int> SelectedWorldIds { get; set; } = new();
    
    // === IGraphWidgetSettings implementation ===
    public GraphColorMode ColorMode { get; set; } = GraphColorMode.PreferredItemColors;
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public bool LegendCollapsed { get; set; } = false;
    public MTLegendPosition LegendPosition { get; set; } = MTLegendPosition.InsideTopLeft;
    public MTGraphType GraphType { get; set; } = MTGraphType.Line;
    public bool ShowXAxisTimestamps { get; set; } = true;
    public bool ShowCrosshair { get; set; } = true;
    public bool ShowGridLines { get; set; } = true;
    public bool ShowCurrentPriceLine { get; set; } = true;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    public bool AutoScrollEnabled { get; set; } = true;
    public int AutoScrollTimeValue { get; set; } = 24;
    public MTTimeUnit AutoScrollTimeUnit { get; set; } = MTTimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    public int TimeRangeValue { get; set; } = 7;
    public MTTimeUnit TimeRangeUnit { get; set; } = MTTimeUnit.Days;
    public NumberFormatConfig NumberFormat { get; set; } = new();
}
