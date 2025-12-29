using Kaleidoscope.Gui.Widgets;
using MTGui.Graph;

namespace Kaleidoscope.Models;

/// <summary>
/// Interface for settings classes that contain graph widget configuration.
/// Implement this interface to enable automatic settings binding with MTGraphWidget.
/// </summary>
public interface IGraphWidgetSettings : IMTGraphSettings
{
    // IGraphWidgetSettings extends IMTGraphSettings from MTGui.Graph
    // No additional members needed - the interface is just for Kaleidoscope-specific typing
}

/// <summary>
/// Shared settings for graph widget display configuration.
/// Used by tools that embed an MTGraphWidget to avoid duplicating settings definitions.
/// Implements IGraphWidgetSettings for automatic binding with MTGraphWidget.
/// </summary>
public class GraphWidgetSettings : IGraphWidgetSettings
{
    // Legend settings
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public MTLegendPosition LegendPosition { get; set; } = MTLegendPosition.Outside;
    
    // Graph type
    public MTGraphType GraphType { get; set; } = MTGraphType.Area;
    
    // Display settings
    public bool ShowXAxisTimestamps { get; set; } = true;
    public bool ShowCrosshair { get; set; } = true;
    public bool ShowGridLines { get; set; } = true;
    public bool ShowCurrentPriceLine { get; set; } = true;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    // Auto-scroll settings
    public bool AutoScrollEnabled { get; set; } = false;
    public int AutoScrollTimeValue { get; set; } = 1;
    public MTTimeUnit AutoScrollTimeUnit { get; set; } = MTTimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    
    // Time range settings
    public int TimeRangeValue { get; set; } = 7;
    public MTTimeUnit TimeRangeUnit { get; set; } = MTTimeUnit.Days;
    
    /// <summary>
    /// Calculates the auto-scroll time range in seconds from value and unit.
    /// </summary>
    public double GetAutoScrollTimeRangeSeconds() => MTTimeUnitExtensions.ToSeconds(AutoScrollTimeUnit, AutoScrollTimeValue);
    
    /// <summary>
    /// Gets the time span for the current time range settings.
    /// </summary>
    public TimeSpan? GetTimeSpan() => TimeRangeSelectorWidget.GetTimeSpan(TimeRangeValue, TimeRangeUnit);
    
    /// <summary>
    /// Copies all graph settings from another IGraphWidgetSettings instance.
    /// </summary>
    public void CopyFrom(IGraphWidgetSettings other)
    {
        LegendWidth = other.LegendWidth;
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
    }
}
