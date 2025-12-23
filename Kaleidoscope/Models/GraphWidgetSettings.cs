using Kaleidoscope.Gui.Widgets;

namespace Kaleidoscope.Models;

/// <summary>
/// Interface for settings classes that contain graph widget configuration.
/// Implement this interface to enable automatic settings binding with ImplotGraphWidget.
/// </summary>
public interface IGraphWidgetSettings
{
    // Legend settings
    float LegendWidth { get; set; }
    float LegendHeightPercent { get; set; }
    bool ShowLegend { get; set; }
    LegendPosition LegendPosition { get; set; }
    
    // Graph type
    GraphType GraphType { get; set; }
    
    // Display settings
    bool ShowXAxisTimestamps { get; set; }
    bool ShowCrosshair { get; set; }
    bool ShowGridLines { get; set; }
    bool ShowCurrentPriceLine { get; set; }
    bool ShowValueLabel { get; set; }
    float ValueLabelOffsetX { get; set; }
    float ValueLabelOffsetY { get; set; }
    
    // Auto-scroll settings
    bool AutoScrollEnabled { get; set; }
    int AutoScrollTimeValue { get; set; }
    AutoScrollTimeUnit AutoScrollTimeUnit { get; set; }
    float AutoScrollNowPosition { get; set; }
    bool ShowControlsDrawer { get; set; }
    
    // Time range settings
    int TimeRangeValue { get; set; }
    TimeRangeUnit TimeRangeUnit { get; set; }
}

/// <summary>
/// Shared settings for graph widget display configuration.
/// Used by tools that embed an ImplotGraphWidget to avoid duplicating settings definitions.
/// Implements IGraphWidgetSettings for automatic binding with ImplotGraphWidget.
/// </summary>
public class GraphWidgetSettings : IGraphWidgetSettings
{
    // Legend settings
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public LegendPosition LegendPosition { get; set; } = LegendPosition.Outside;
    
    // Graph type
    public GraphType GraphType { get; set; } = GraphType.Area;
    
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
    public AutoScrollTimeUnit AutoScrollTimeUnit { get; set; } = AutoScrollTimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    
    // Time range settings
    public int TimeRangeValue { get; set; } = 7;
    public TimeRangeUnit TimeRangeUnit { get; set; } = TimeRangeUnit.Days;
    
    /// <summary>
    /// Calculates the auto-scroll time range in seconds from value and unit.
    /// </summary>
    public double GetAutoScrollTimeRangeSeconds() => AutoScrollTimeUnit.ToSeconds(AutoScrollTimeValue);
    
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
