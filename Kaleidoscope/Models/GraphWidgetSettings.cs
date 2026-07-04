using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Common;
using Kaleidoscope.Gui.Widgets.Graph;

namespace Kaleidoscope.Models;

public enum GraphColorMode
{
    /// <summary>Don't use preferred colors - use custom series colors or default palette.</summary>
    DontUse = 0,
    /// <summary>Use preferred item colors from configuration.</summary>
    PreferredItemColors = 1,
    /// <summary>Use preferred character colors from configuration.</summary>
    PreferredCharacterColors = 2
}

/// <summary>
/// Interface for settings classes that contain graph widget configuration.
/// Implement this interface to enable automatic settings binding with GraphWidget.
/// </summary>
public interface IGraphWidgetSettings : IGraphSettings
{
    GraphColorMode ColorMode { get; set; }
}

/// <summary>
/// Shared settings for graph widget display configuration.
/// Used by tools that embed an GraphWidget to avoid duplicating settings definitions.
/// Implements IGraphWidgetSettings for automatic binding with GraphWidget.
/// </summary>
/// <remarks>
/// The graph-display properties below are intentionally duplicated in the sibling settings classes
/// ItemGraphSettings, DataToolSettings, InventoryValueSettings and ItemSalesTrackingSettings. Each
/// class is serialized independently into the user's config JSON, so the flat property layout must be
/// preserved; do NOT collapse these into a shared nested object, as that would change persisted JSON
/// paths and break loading of existing configs.
/// </remarks>
public sealed class GraphWidgetSettings : IGraphWidgetSettings
{
    public GraphColorMode ColorMode { get; set; } = GraphColorMode.PreferredItemColors;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public bool LegendCollapsed { get; set; } = false;
    public LegendPosition LegendPosition { get; set; } = LegendPosition.InsideTopLeft;
    public GraphType GraphType { get; set; } = GraphType.Area;
    public bool ShowXAxisTimestamps { get; set; } = true;
    public bool ShowCrosshair { get; set; } = true;
    public bool ShowGridLines { get; set; } = true;
    public bool ShowCurrentPriceLine { get; set; } = true;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    public bool AutoScrollEnabled { get; set; } = false;
    public int AutoScrollTimeValue { get; set; } = 1;
    public TimeUnit AutoScrollTimeUnit { get; set; } = TimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    public int TimeRangeValue { get; set; } = 7;
    public TimeUnit TimeRangeUnit { get; set; } = TimeUnit.Days;
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    public double GetAutoScrollTimeRangeSeconds() => TimeUnitExtensions.ToSeconds(AutoScrollTimeUnit, AutoScrollTimeValue);
    
    public TimeSpan? GetTimeSpan() => TimeRangeSelectorWidget.GetTimeSpan(TimeRangeValue, TimeRangeUnit);
    
    public void CopyFrom(IGraphWidgetSettings other)
    {
        ColorMode = other.ColorMode;
        LegendHeightPercent = other.LegendHeightPercent;
        ShowLegend = other.ShowLegend;
        LegendPosition = other.LegendPosition;
        GraphType = other.GraphType;
        ShowXAxisTimestamps = other.ShowXAxisTimestamps;
        ShowCrosshair = other.ShowCrosshair;
        ShowGridLines = other.ShowGridLines;
        ShowCurrentPriceLine = other.ShowCurrentPriceLine;
        ShowValueLabel = other.ShowValueLabel;
        ValueLabelOffsetX = other.ValueLabelOffsetX;
        ValueLabelOffsetY = other.ValueLabelOffsetY;
        AutoScrollEnabled = other.AutoScrollEnabled;
        AutoScrollTimeValue = other.AutoScrollTimeValue;
        AutoScrollTimeUnit = other.AutoScrollTimeUnit;
        AutoScrollNowPosition = other.AutoScrollNowPosition;
        ShowControlsDrawer = other.ShowControlsDrawer;
        TimeRangeValue = other.TimeRangeValue;
        TimeRangeUnit = other.TimeRangeUnit;
        NumberFormat.CopyFrom(other.NumberFormat);
    }
}
