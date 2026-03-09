using Kaleidoscope.Gui.Widgets.Common;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Configuration options for Graph widget.
/// </summary>
public class GraphConfig
{
    /// <summary>
    /// The minimum value for the Y-axis. Default is 0.
    /// </summary>
    public float MinValue { get; set; } = 0f;

    /// <summary>
    /// The maximum value for the Y-axis. Default is 100 million.
    /// </summary>
    public float MaxValue { get; set; } = 100_000_000f;

    /// <summary>
    /// The ID suffix for the ImPlot elements.
    /// </summary>
    public string PlotId { get; set; } = "sampleplot";

    /// <summary>
    /// Text to display when there is no data.
    /// </summary>
    public string NoDataText { get; set; } = "No data yet.";

    /// <summary>
    /// Epsilon for floating point comparisons.
    /// </summary>
    public float FloatEpsilon { get; set; } = 0.0001f;

    /// <summary>
    /// Whether to show a value label near the latest point.
    /// </summary>
    public bool ShowValueLabel { get; set; } = false;

    /// <summary>
    /// X offset for the value label position (negative = left, positive = right).
    /// </summary>
    public float ValueLabelOffsetX { get; set; } = 0f;

    /// <summary>
    /// Y offset for the value label position (negative = up, positive = down).
    /// </summary>
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    /// <summary>
    /// Width of the scrollable legend panel in multi-series mode.
    /// </summary>
    public float LegendWidth { get; set; } = 140f;
    
    /// <summary>
    /// Whether to show the legend panel in multi-series mode.
    /// </summary>
    public bool ShowLegend { get; set; } = true;
    
    /// <summary>
    /// The position of the legend (inside or outside the graph).
    /// </summary>
    public LegendPosition LegendPosition { get; set; } = LegendPosition.InsideTopLeft;
    
    /// <summary>
    /// Maximum height of the inside legend as a percentage of plot height (10-80%).
    /// </summary>
    public float LegendHeightPercent { get; set; } = 25f;
    
    /// <summary>
    /// Whether the legend is collapsed (showing only a toggle button).
    /// </summary>
    public bool LegendCollapsed { get; set; } = false;
    
    /// <summary>
    /// The type of graph to render (Area, Line, Stairs, Bars).
    /// </summary>
    public GraphType GraphType { get; set; } = GraphType.Area;
    
    /// <summary>
    /// Whether to show X-axis time labels with timestamps.
    /// </summary>
    public bool ShowXAxisTimestamps { get; set; } = true;
    
    /// <summary>
    /// Whether to show crosshair lines on hover (trading platform style).
    /// </summary>
    public bool ShowCrosshair { get; set; } = true;
    
    /// <summary>
    /// Whether to show horizontal grid lines for price levels.
    /// </summary>
    public bool ShowGridLines { get; set; } = true;
    
    /// <summary>
    /// Whether to show the current price horizontal line.
    /// </summary>
    public bool ShowCurrentPriceLine { get; set; } = true;
    
    /// <summary>
    /// Whether auto-scroll (follow mode) is enabled.
    /// When enabled, the graph automatically scrolls to show the most recent data.
    /// </summary>
    public bool AutoScrollEnabled { get; set; } = false;
    
    /// <summary>
    /// The numeric value for auto-scroll time range.
    /// </summary>
    public int AutoScrollTimeValue { get; set; } = 1;
    
    /// <summary>
    /// The unit for auto-scroll time range.
    /// </summary>
    public TimeUnit AutoScrollTimeUnit { get; set; } = TimeUnit.Hours;
    
    /// <summary>
    /// Calculates the auto-scroll time range in seconds.
    /// </summary>
    public double GetAutoScrollTimeRangeSeconds() => AutoScrollTimeUnit.ToSeconds(AutoScrollTimeValue);
    
    /// <summary>
    /// Whether to show the controls drawer panel.
    /// </summary>
    public bool ShowControlsDrawer { get; set; } = true;
    
    /// <summary>
    /// Position of "now" on the X-axis when auto-scrolling (0-100%).
    /// 0% = now at left edge, 50% = centered, 100% = now at right edge.
    /// </summary>
    public float AutoScrollNowPosition { get; set; } = 75f;
    
    /// <summary>
    /// Whether to simulate real-time updates by extending the last data point to "now".
    /// When enabled, the graph will continuously update to show the current time as if 
    /// the last known data value is still current. This creates a smooth real-time 
    /// visualization even when data updates are infrequent.
    /// </summary>
    public bool SimulateRealTimeUpdates { get; set; } = true;
    
    /// <summary>
    /// Number format configuration for numeric values displayed on the graph.
    /// </summary>
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    /// <summary>
    /// Style configuration for the graph.
    /// </summary>
    public GraphStyleConfig Style { get; set; } = new();
}

/// <summary>
/// Interface for settings classes that contain graph widget configuration.
/// Implement this interface to enable automatic settings binding with Graph.
/// </summary>
public interface IGraphSettings
{
    // Legend settings
    float LegendWidth { get; set; }
    float LegendHeightPercent { get; set; }
    bool ShowLegend { get; set; }
    bool LegendCollapsed { get; set; }
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
    TimeUnit AutoScrollTimeUnit { get; set; }
    float AutoScrollNowPosition { get; set; }
    bool ShowControlsDrawer { get; set; }
    
    // Time range settings
    int TimeRangeValue { get; set; }
    TimeUnit TimeRangeUnit { get; set; }
    
    // Number format settings
    NumberFormatConfig NumberFormat { get; set; }
}

/// <summary>
/// Default implementation of IGraphSettings.
/// Used by tools that embed an Graph to avoid duplicating settings definitions.
/// </summary>
public class MTGraphSettings : IGraphSettings
{
    // Legend settings
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public bool LegendCollapsed { get; set; } = false;
    public LegendPosition LegendPosition { get; set; } = LegendPosition.InsideTopLeft;
    
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
    public TimeUnit AutoScrollTimeUnit { get; set; } = TimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    
    // Time range settings
    public int TimeRangeValue { get; set; } = 7;
    public TimeUnit TimeRangeUnit { get; set; } = TimeUnit.Days;
    
    // Number format settings
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    /// <summary>
    /// Calculates the auto-scroll time range in seconds from value and unit.
    /// </summary>
    public double GetAutoScrollTimeRangeSeconds() => AutoScrollTimeUnit.ToSeconds(AutoScrollTimeValue);
    
    /// <summary>
    /// Gets the time span for the current time range settings.
    /// Returns null for "All" time unit.
    /// </summary>
    public TimeSpan? GetTimeSpan()
    {
        if (TimeRangeUnit == TimeUnit.All)
            return null;
            
        var seconds = TimeRangeUnit.ToSeconds(TimeRangeValue);
        return TimeSpan.FromSeconds(seconds);
    }
    
    /// <summary>
    /// Copies all graph settings from another IGraphSettings instance.
    /// </summary>
    public void CopyFrom(IGraphSettings other)
    {
        LegendWidth = other.LegendWidth;
        LegendHeightPercent = other.LegendHeightPercent;
        ShowLegend = other.ShowLegend;
        LegendCollapsed = other.LegendCollapsed;
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
