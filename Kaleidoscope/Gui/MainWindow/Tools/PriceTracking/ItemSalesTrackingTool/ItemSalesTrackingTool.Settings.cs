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
public sealed partial class ItemSalesTrackingTool
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

        var showActionButtons = Settings.ShowActionButtons;
        if (ImGui.Checkbox("Show Action Buttons", ref showActionButtons))
        {
            Settings.ShowActionButtons = showActionButtons;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show or hide the item selector, world scope selector, and refresh button.");
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

        EnsureWorldSelectionWidgetInitialized(worldData);
        
        // Use wider width for settings panel
        _worldSelectionWidget!.Width = 280f;

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
            // Tool-specific
            ["SelectedItemIds"] = _itemCombo.SelectedItemIds.ToList(),
            ["MaxHistoryEntries"] = Settings.MaxHistoryEntries,
            ["FilterOutliers"] = Settings.FilterOutliers,
            ["ScopeMode"] = (int)Settings.ScopeMode,
            ["SelectedRegions"] = Settings.SelectedRegions.ToList(),
            ["SelectedDataCenters"] = Settings.SelectedDataCenters.ToList(),
            ["SelectedWorldIds"] = Settings.SelectedWorldIds.ToList(),
            ["ShowActionButtons"] = Settings.ShowActionButtons,
            
            // Graph settings
            ["ColorMode"] = (int)Settings.ColorMode,
            ["LegendWidth"] = Settings.LegendWidth,
            ["LegendHeightPercent"] = Settings.LegendHeightPercent,
            ["ShowLegend"] = Settings.ShowLegend,
            ["LegendCollapsed"] = Settings.LegendCollapsed,
            ["LegendPosition"] = (int)Settings.LegendPosition,
            ["GraphType"] = (int)Settings.GraphType,
            ["ShowXAxisTimestamps"] = Settings.ShowXAxisTimestamps,
            ["ShowCrosshair"] = Settings.ShowCrosshair,
            ["ShowGridLines"] = Settings.ShowGridLines,
            ["ShowCurrentPriceLine"] = Settings.ShowCurrentPriceLine,
            ["ShowValueLabel"] = Settings.ShowValueLabel,
            ["ValueLabelOffsetX"] = Settings.ValueLabelOffsetX,
            ["ValueLabelOffsetY"] = Settings.ValueLabelOffsetY,
            ["AutoScrollEnabled"] = Settings.AutoScrollEnabled,
            ["AutoScrollTimeValue"] = Settings.AutoScrollTimeValue,
            ["AutoScrollTimeUnit"] = (int)Settings.AutoScrollTimeUnit,
            ["AutoScrollNowPosition"] = Settings.AutoScrollNowPosition,
            ["ShowControlsDrawer"] = Settings.ShowControlsDrawer,
            ["TimeRangeValue"] = Settings.TimeRangeValue,
            ["TimeRangeUnit"] = (int)Settings.TimeRangeUnit,
            ["NumberFormatStyle"] = (int)Settings.NumberFormat.Style,
            ["NumberFormatDecimalPlaces"] = Settings.NumberFormat.DecimalPlaces
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
        Settings.ShowActionButtons = GetSetting(settings, "ShowActionButtons", true);

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
        
        // Graph settings
        Settings.ColorMode = (GraphColorMode)GetSetting(settings, "ColorMode", (int)Settings.ColorMode);
        Settings.LegendWidth = GetSetting(settings, "LegendWidth", Settings.LegendWidth);
        Settings.LegendHeightPercent = GetSetting(settings, "LegendHeightPercent", Settings.LegendHeightPercent);
        Settings.ShowLegend = GetSetting(settings, "ShowLegend", Settings.ShowLegend);
        Settings.LegendCollapsed = GetSetting(settings, "LegendCollapsed", Settings.LegendCollapsed);
        Settings.LegendPosition = (MTLegendPosition)GetSetting(settings, "LegendPosition", (int)Settings.LegendPosition);
        Settings.GraphType = (MTGraphType)GetSetting(settings, "GraphType", (int)Settings.GraphType);
        Settings.ShowXAxisTimestamps = GetSetting(settings, "ShowXAxisTimestamps", Settings.ShowXAxisTimestamps);
        Settings.ShowCrosshair = GetSetting(settings, "ShowCrosshair", Settings.ShowCrosshair);
        Settings.ShowGridLines = GetSetting(settings, "ShowGridLines", Settings.ShowGridLines);
        Settings.ShowCurrentPriceLine = GetSetting(settings, "ShowCurrentPriceLine", Settings.ShowCurrentPriceLine);
        Settings.ShowValueLabel = GetSetting(settings, "ShowValueLabel", Settings.ShowValueLabel);
        Settings.ValueLabelOffsetX = GetSetting(settings, "ValueLabelOffsetX", Settings.ValueLabelOffsetX);
        Settings.ValueLabelOffsetY = GetSetting(settings, "ValueLabelOffsetY", Settings.ValueLabelOffsetY);
        Settings.AutoScrollEnabled = GetSetting(settings, "AutoScrollEnabled", Settings.AutoScrollEnabled);
        Settings.AutoScrollTimeValue = GetSetting(settings, "AutoScrollTimeValue", Settings.AutoScrollTimeValue);
        Settings.AutoScrollTimeUnit = (MTTimeUnit)GetSetting(settings, "AutoScrollTimeUnit", (int)Settings.AutoScrollTimeUnit);
        Settings.AutoScrollNowPosition = GetSetting(settings, "AutoScrollNowPosition", Settings.AutoScrollNowPosition);
        Settings.ShowControlsDrawer = GetSetting(settings, "ShowControlsDrawer", Settings.ShowControlsDrawer);
        Settings.TimeRangeValue = GetSetting(settings, "TimeRangeValue", Settings.TimeRangeValue);
        Settings.TimeRangeUnit = (MTTimeUnit)GetSetting(settings, "TimeRangeUnit", (int)Settings.TimeRangeUnit);
        
        if (settings.ContainsKey("NumberFormatStyle"))
        {
            Settings.NumberFormat.Style = (NumberFormatStyle)GetSetting(settings, "NumberFormatStyle", (int)Settings.NumberFormat.Style);
            Settings.NumberFormat.DecimalPlaces = GetSetting(settings, "NumberFormatDecimalPlaces", Settings.NumberFormat.DecimalPlaces);
        }
        
        var selectedIds = _itemCombo.SelectedItemIds.ToList();
        LogDebug($"ImportToolSettings: {selectedIds.Count} items selected after import");
        if (selectedIds.Count > 0)
        {
            LogDebug($"ImportToolSettings: Triggering initial fetch for {selectedIds.Count} items");
            _ = FetchHistoryForItemsAsync(selectedIds);
        }
    }
}

/// <summary>
/// Instance settings for ItemSalesTrackingTool.
/// Implements IGraphWidgetSettings for automatic graph widget binding.
/// </summary>
public sealed class ItemSalesTrackingSettings : IGraphWidgetSettings
{
    // Tool-specific settings
    public int MaxHistoryEntries { get; set; } = 100;
    public bool FilterOutliers { get; set; } = true;
    public WorldSelectionMode ScopeMode { get; set; } = WorldSelectionMode.Worlds;
    public HashSet<string> SelectedRegions { get; set; } = new();
    public HashSet<string> SelectedDataCenters { get; set; } = new();
    public HashSet<int> SelectedWorldIds { get; set; } = new();
    
    /// <summary>
    /// Whether to show the action buttons row (item selector, world scope, refresh).
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;
    
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
