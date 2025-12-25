using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Represents a single data series for graph rendering.
/// Abstracts over both index-based and time-based data sources.
/// </summary>
internal sealed class GraphSeriesData
{
    /// <summary>
    /// Display name for this series (used in legends and tooltips).
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// X-axis values (either indices or seconds from start time).
    /// Array may be larger than PointCount for pooling efficiency.
    /// </summary>
    public required double[] XValues { get; init; }
    
    /// <summary>
    /// Y-axis values corresponding to each X value.
    /// Array may be larger than PointCount for pooling efficiency.
    /// </summary>
    public required double[] YValues { get; init; }
    
    /// <summary>
    /// Actual number of valid data points (XValues/YValues may be larger for pooling).
    /// </summary>
    public int PointCount { get; init; }
    
    /// <summary>
    /// Color for this series (RGB).
    /// </summary>
    public Vector3 Color { get; init; } = new(1f, 1f, 1f);
    
    /// <summary>
    /// Whether this series should be rendered (not hidden by user).
    /// </summary>
    public bool Visible { get; init; } = true;
}

/// <summary>
/// Prepared data for graph rendering, including computed bounds and all series.
/// </summary>
internal sealed class PreparedGraphData
{
    /// <summary>
    /// All series to render.
    /// </summary>
    public required IReadOnlyList<GraphSeriesData> Series { get; init; }
    
    /// <summary>
    /// Minimum X value across all visible series.
    /// </summary>
    public double XMin { get; set; }
    
    /// <summary>
    /// Maximum X value across all visible series (including padding).
    /// </summary>
    public double XMax { get; set; }
    
    /// <summary>
    /// Minimum Y value across all visible series.
    /// </summary>
    public double YMin { get; set; }
    
    /// <summary>
    /// Maximum Y value across all visible series.
    /// </summary>
    public double YMax { get; set; }
    
    /// <summary>
    /// Whether this is time-based data (true) or index-based (false).
    /// </summary>
    public bool IsTimeBased { get; init; }
    
    /// <summary>
    /// For time-based data: the reference start time for X-axis formatting.
    /// </summary>
    public DateTime StartTime { get; init; }
    
    /// <summary>
    /// Total time span in seconds (for time-based data).
    /// Mutable to allow real-time updates for auto-scroll.
    /// </summary>
    public double TotalTimeSpan { get; set; }
    
    /// <summary>
    /// Whether this graph has multiple visible series (affects rendering decisions like bar width).
    /// </summary>
    public bool HasMultipleSeries => Series.Count(s => s.Visible) > 1;
    
    /// <summary>
    /// Whether this graph has multiple series total (including hidden), affects legend display.
    /// This ensures the legend remains visible when series are hidden, allowing users to re-enable them.
    /// </summary>
    public bool HasMultipleSeriesTotal => Series.Count > 1;
}

/// <summary>
/// Specifies where the legend should be positioned in the graph.
/// </summary>
public enum LegendPosition
{
    /// <summary>
    /// Legend is drawn outside the graph area (to the right).
    /// </summary>
    Outside,
    
    /// <summary>
    /// Legend is drawn inside the graph area (top-left corner).
    /// </summary>
    InsideTopLeft,
    
    /// <summary>
    /// Legend is drawn inside the graph area (top-right corner).
    /// </summary>
    InsideTopRight,
    
    /// <summary>
    /// Legend is drawn inside the graph area (bottom-left corner).
    /// </summary>
    InsideBottomLeft,
    
    /// <summary>
    /// Legend is drawn inside the graph area (bottom-right corner).
    /// </summary>
    InsideBottomRight
}

/// <summary>
/// A reusable graph widget for displaying numerical sample data.
/// Renders using ImPlot with a trading platform (Binance-style) aesthetic.
/// Implements ISettingsProvider to expose graph settings for automatic inclusion in tool settings.
/// </summary>
public class ImplotGraphWidget : ISettingsProvider
{
    // Trading platform color palette - vibrant and bright
    private static class ChartColors
    {
        // Background colors
        public static readonly Vector4 PlotBackground = new(0.08f, 0.09f, 0.10f, 1f);
        public static readonly Vector4 FrameBackground = new(0.06f, 0.07f, 0.08f, 1f);
        
        // Grid colors
        public static readonly Vector4 GridLine = new(0.18f, 0.20f, 0.22f, 0.6f);
        public static readonly Vector4 AxisLine = new(0.25f, 0.28f, 0.30f, 1f);
        
        // Price movement colors - bright and vibrant
        public static readonly Vector4 Bullish = new(0.20f, 0.90f, 0.40f, 1f);      // Bright Green
        public static readonly Vector4 BullishFillTop = new(0.20f, 0.90f, 0.40f, 0.60f);    // Same color, vibrant fill
        public static readonly Vector4 BullishFillBottom = new(0.20f, 0.90f, 0.40f, 0.05f); // Same color, very transparent
        public static readonly Vector4 Bearish = new(1.0f, 0.25f, 0.25f, 1f);       // Bright Red
        public static readonly Vector4 BearishFillTop = new(1.0f, 0.25f, 0.25f, 0.60f);     // Same color, vibrant fill
        public static readonly Vector4 BearishFillBottom = new(1.0f, 0.25f, 0.25f, 0.05f);  // Same color, very transparent
        public static readonly Vector4 Neutral = new(1.0f, 0.85f, 0.0f, 1f);        // Bright Yellow
        
        // Crosshair and tooltip
        public static readonly Vector4 Crosshair = new(0.55f, 0.58f, 0.62f, 0.8f);
        public static readonly Vector4 TooltipBackground = new(0.12f, 0.14f, 0.16f, 0.95f);
        public static readonly Vector4 TooltipBorder = new(0.30f, 0.32f, 0.35f, 1f);
        
        // Current price line
        public static readonly Vector4 CurrentPriceLine = new(1.0f, 0.85f, 0.0f, 0.9f);
        
        // Text colors
        public static readonly Vector4 TextPrimary = new(0.90f, 0.92f, 0.94f, 1f);
        public static readonly Vector4 TextSecondary = new(0.55f, 0.58f, 0.62f, 1f);
    }

    /// <summary>
    /// Configuration options for the graph.
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
    }

    private readonly GraphConfig _config;
    
    // === ISettingsProvider implementation fields ===
    
    /// <summary>
    /// Optional bound settings object for automatic settings synchronization.
    /// When set, the widget will read and write settings from this object.
    /// </summary>
    private IGraphWidgetSettings? _boundSettings;
    
    /// <summary>
    /// Callback invoked when a setting is changed via DrawSettings().
    /// </summary>
    private Action? _onSettingsChanged;
    
    /// <summary>
    /// Display name for this component's settings section.
    /// </summary>
    private string _settingsName = "Graph Settings";
    
    /// <summary>
    /// Whether to show legend settings in DrawSettings (requires multi-series mode).
    /// </summary>
    private bool _showLegendSettings = true;
    
    /// <summary>
    /// Display names for legend positions.
    /// </summary>
    private static readonly string[] LegendPositionNames = { "Outside (right)", "Inside Top-Left", "Inside Top-Right", "Inside Bottom-Left", "Inside Bottom-Right" };
    
    /// <summary>
    /// Display names for auto-scroll time units.
    /// </summary>
    private static readonly string[] AutoScrollTimeUnitNames = { "Seconds", "Minutes", "Hours", "Days", "Weeks" };
    
    /// <summary>
    /// Set of series names that are currently hidden.
    /// </summary>
    private readonly HashSet<string> _hiddenSeries = new();
    
    /// <summary>
    /// Scroll offset for the inside legend when there are too many series.
    /// </summary>
    private float _insideLegendScrollOffset = 0f;
    
    /// <summary>
    /// Cached legend bounds from the previous frame for input blocking.
    /// </summary>
    private (Vector2 min, Vector2 max, bool valid) _cachedLegendBounds = (Vector2.Zero, Vector2.Zero, false);
    
    /// <summary>
    /// Whether the controls drawer is currently open.
    /// </summary>
    private bool _controlsDrawerOpen = false;
    
    // === Performance optimization caches ===
    /// <summary>
    /// Cached sorted series list to avoid LINQ sorting every frame.
    /// </summary>
    private readonly List<GraphSeriesData> _sortedSeriesCache = new();
    
    /// <summary>
    /// Cached labels for DrawValueLabels to avoid LINQ allocations.
    /// </summary>
    private readonly List<(string Name, float Value, Vector3 Color)> _valueLabelCache = new();
    
    /// <summary>
    /// Hash of the last series collection used to invalidate caches.
    /// </summary>
    private int _lastSeriesHash = 0;
    
    /// <summary>
    /// Cached controls drawer bounds from the previous frame.
    /// </summary>
    private (Vector2 min, Vector2 max, bool valid) _cachedControlsDrawerBounds = (Vector2.Zero, Vector2.Zero, false);
    
    /// <summary>
    /// Names for auto-scroll time unit buttons.
    /// </summary>
    private static readonly string[] TimeUnitNames = { "sec", "min", "hr", "day", "wk" };
    
    // === Array pooling for PrepareTimeBasedData ===
    /// <summary>
    /// Pooled arrays for X values, keyed by series name.
    /// </summary>
    private readonly Dictionary<string, double[]> _pooledXArrays = new();
    
    /// <summary>
    /// Pooled arrays for Y values, keyed by series name.
    /// </summary>
    private readonly Dictionary<string, double[]> _pooledYArrays = new();
    
    // === PreparedGraphData caching to avoid recomputing every frame ===
    /// <summary>
    /// Cached prepared graph data to avoid recomputing every frame.
    /// </summary>
    private PreparedGraphData? _cachedPreparedData;
    
    /// <summary>
    /// Reference to the last series data used to detect changes.
    /// </summary>
    private IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? _lastSeriesData;
    
    /// <summary>
    /// Reference to the last series data with colors used to detect changes.
    /// </summary>
    private IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? _lastSeriesDataWithColors;
    
    /// <summary>
    /// Hash of hidden series to detect visibility changes.
    /// </summary>
    private int _lastHiddenSeriesHash;
    
    /// <summary>
    /// Last auto-scroll enabled state for cache invalidation.
    /// </summary>
    private bool _lastAutoScrollEnabled;
    
    /// <summary>
    /// Last time the prepared data was computed (for auto-scroll time updates).
    /// </summary>
    private DateTime _lastPreparedDataTime = DateTime.MinValue;
    
    // === Virtualized data loading ===
    /// <summary>
    /// The currently loaded data time range (start, end).
    /// Used to determine if we need to request more data.
    /// </summary>
    private (DateTime start, DateTime end)? _loadedDataRange;
    
    /// <summary>
    /// The overall data time range (earliest to latest available data).
    /// Set by the data provider to allow proper scrollbar behavior.
    /// </summary>
    private (DateTime earliest, DateTime latest)? _availableDataRange;
    
    /// <summary>
    /// Buffer factor for pre-loading data outside visible range (e.g., 1.5 = load 50% extra on each side).
    /// </summary>
    private const double LoadBufferFactor = 1.5;
    
    /// <summary>
    /// Gets whether the mouse is currently over the inside legend (based on cached bounds).
    /// Used to block ImPlot input when interacting with the legend.
    /// </summary>
    public bool IsMouseOverInsideLegend
    {
        get
        {
            if (!_cachedLegendBounds.valid || _config.LegendPosition == LegendPosition.Outside)
                return false;
            var mousePos = ImGui.GetMousePos();
            return mousePos.X >= _cachedLegendBounds.min.X && mousePos.X <= _cachedLegendBounds.max.X &&
                   mousePos.Y >= _cachedLegendBounds.min.Y && mousePos.Y <= _cachedLegendBounds.max.Y;
        }
    }
    
    /// <summary>
    /// Gets whether the mouse is currently over the controls drawer (based on cached bounds).
    /// Used to block ImPlot input when interacting with the controls.
    /// </summary>
    public bool IsMouseOverControlsDrawer
    {
        get
        {
            if (!_cachedControlsDrawerBounds.valid)
                return false;
            var mousePos = ImGui.GetMousePos();
            return mousePos.X >= _cachedControlsDrawerBounds.min.X && mousePos.X <= _cachedControlsDrawerBounds.max.X &&
                   mousePos.Y >= _cachedControlsDrawerBounds.min.Y && mousePos.Y <= _cachedControlsDrawerBounds.max.Y;
        }
    }
    
    /// <summary>
    /// Gets whether the mouse is over any overlay element (legend or controls drawer).
    /// </summary>
    public bool IsMouseOverOverlay => IsMouseOverInsideLegend || IsMouseOverControlsDrawer;
    
    /// <summary>
    /// Checks whether a point is inside the legend bounds.
    /// </summary>
    private bool IsPointInLegend(Vector2 point)
    {
        if (!_cachedLegendBounds.valid || _config.LegendPosition == LegendPosition.Outside)
            return false;
        return point.X >= _cachedLegendBounds.min.X && point.X <= _cachedLegendBounds.max.X &&
               point.Y >= _cachedLegendBounds.min.Y && point.Y <= _cachedLegendBounds.max.Y;
    }
    
    /// <summary>
    /// Checks whether a point is inside the controls drawer bounds.
    /// </summary>
    private bool IsPointInControlsDrawer(Vector2 point)
    {
        if (!_cachedControlsDrawerBounds.valid)
            return false;
        return point.X >= _cachedControlsDrawerBounds.min.X && point.X <= _cachedControlsDrawerBounds.max.X &&
               point.Y >= _cachedControlsDrawerBounds.min.Y && point.Y <= _cachedControlsDrawerBounds.max.Y;
    }
    
    /// <summary>
    /// Event fired when auto-scroll settings are changed via the controls drawer.
    /// Parameters: (bool autoScrollEnabled, int timeValue, TimeUnit timeUnit, float nowPosition)
    /// </summary>
    public event Action<bool, int, TimeUnit, float>? OnAutoScrollSettingsChanged;
    
    /// <summary>
    /// Delegate for virtualized data loading.
    /// Called when the widget needs data for a specific time window.
    /// Parameters: (DateTime windowStart, DateTime windowEnd) - the time range to load data for
    /// Returns: List of series data within the requested window
    /// </summary>
    public delegate IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? 
        WindowedDataProvider(DateTime windowStart, DateTime windowEnd);
    
    /// <summary>
    /// Optional data provider for virtualized loading.
    /// When set, the widget will request only data within the visible window.
    /// </summary>
    public WindowedDataProvider? DataProvider { get; set; }
    
    /// <summary>
    /// Sets the available data time range (used for proper scrollbar behavior in virtualized mode).
    /// Call this when using DataProvider to inform the widget of the full data extent.
    /// </summary>
    public void SetAvailableDataRange(DateTime earliest, DateTime latest)
    {
        _availableDataRange = (earliest, latest);
    }
    
    /// <summary>
    /// Gets or sets the hidden series names.
    /// </summary>
    public IReadOnlyCollection<string> HiddenSeries => _hiddenSeries;
    
    /// <summary>
    /// Sets the hidden series from an external collection.
    /// </summary>
    public void SetHiddenSeries(IEnumerable<string>? seriesNames)
    {
        _hiddenSeries.Clear();
        if (seriesNames != null)
        {
            foreach (var name in seriesNames)
                _hiddenSeries.Add(name);
        }
    }
    
    /// <summary>
    /// Invalidates the cached prepared graph data, forcing recomputation on the next draw.
    /// Call this when the underlying data changes.
    /// </summary>
    public void InvalidatePreparedDataCache()
    {
        _cachedPreparedData = null;
        _lastSeriesData = null;
    }
    
    /// <summary>
    /// Formatter delegate for X-axis tick labels with time values.
    /// Shows only time (HH:mm) if all visible labels are on the same day, otherwise date+time (M/d HH:mm).
    /// </summary>
    private static readonly unsafe ImPlotFormatter XAxisTimeFormatter = (double value, byte* buff, int size, void* userData) =>
    {
        // value is seconds from the start time, userData contains the start time ticks
        var startTicks = (long)userData;
        var startTime = new DateTime(startTicks);
        var time = startTime.AddSeconds(value).ToLocalTime();
        
        // Check if the visible X-axis range spans a single day
        var plotLimits = ImPlot.GetPlotLimits();
        var visibleMinTime = startTime.AddSeconds(plotLimits.X.Min).ToLocalTime();
        var visibleMaxTime = startTime.AddSeconds(plotLimits.X.Max).ToLocalTime();
        var isSameDay = visibleMinTime.Date == visibleMaxTime.Date;
        
        // If all visible labels are on the same day, show only time; otherwise show date+time
        var format = isSameDay ? "HH:mm" : "M/d HH:mm";
        var formatted = time.ToString(format);
        var len = Math.Min(formatted.Length, size - 1);
        for (var i = 0; i < len; i++)
            buff[i] = (byte)formatted[i];
        buff[len] = 0;
        return len;
    };
    
    /// <summary>
    /// Formatter delegate for Y-axis tick labels with abbreviated notation (K, M, B).
    /// </summary>
    private static readonly unsafe ImPlotFormatter YAxisFormatter = (double value, byte* buff, int size, void* userData) =>
    {
        var formatted = FormatUtils.FormatAbbreviated(value);
        var len = Math.Min(formatted.Length, size - 1);
        for (var i = 0; i < len; i++)
            buff[i] = (byte)formatted[i];
        buff[len] = 0;
        return len;
    };
    
    /// <summary>
    /// Applies trading platform style colors to the plot.
    /// Must be called before BeginPlot.
    /// </summary>
    private static void PushChartStyle()
    {
        // Plot frame and background - using correct Dalamud ImPlot enum names
        ImPlot.PushStyleColor(ImPlotCol.Bg, ChartColors.PlotBackground);
        ImPlot.PushStyleColor(ImPlotCol.FrameBg, ChartColors.FrameBackground);
        
        // Line styling - bullish green by default
        ImPlot.PushStyleColor(ImPlotCol.Line, ChartColors.Bullish);
        ImPlot.PushStyleColor(ImPlotCol.Fill, ChartColors.BullishFillTop);
        
        // Crosshair
        ImPlot.PushStyleColor(ImPlotCol.Crosshairs, ChartColors.Crosshair);
        
        // Style variables
        ImPlot.PushStyleVar(ImPlotStyleVar.LineWeight, 2f);
        ImPlot.PushStyleVar(ImPlotStyleVar.FillAlpha, 0.35f);
    }
    
    /// <summary>
    /// Pops the trading platform style colors.
    /// Must be called after EndPlot.
    /// </summary>
    private static void PopChartStyle()
    {
        ImPlot.PopStyleVar(2);
        ImPlot.PopStyleColor(5);
    }
    
    /// <summary>
    /// Draws a horizontal price level line with label (trading platform style).
    /// </summary>
    private static void DrawPriceLine(double yValue, string label, Vector4 color, float thickness = 1f, bool dashed = false)
    {
        var drawList = ImPlot.GetPlotDrawList();
        var plotLimits = ImPlot.GetPlotLimits();
        
        // Get pixel positions
        var p1 = ImPlot.PlotToPixels(plotLimits.X.Min, yValue);
        var p2 = ImPlot.PlotToPixels(plotLimits.X.Max, yValue);
        
        var colorU32 = ImGui.GetColorU32(color);
        
        if (dashed)
        {
            // Draw dashed line
            const float dashLength = 6f;
            const float gapLength = 4f;
            var totalLength = p2.X - p1.X;
            var x = p1.X;
            while (x < p2.X)
            {
                var endX = Math.Min(x + dashLength, p2.X);
                drawList.AddLine(new Vector2(x, p1.Y), new Vector2(endX, p1.Y), colorU32, thickness);
                x += dashLength + gapLength;
            }
        }
        else
        {
            drawList.AddLine(p1, p2, colorU32, thickness);
        }
        
        // Draw price label on the right (clipped to plot area so it goes under the Y-axis)
        if (!string.IsNullOrEmpty(label))
        {
            var plotPos = ImPlot.GetPlotPos();
            var plotSize = ImPlot.GetPlotSize();
            var labelSize = ImGui.CalcTextSize(label);
            var labelPos = new Vector2(p2.X - labelSize.X - 4, p2.Y - labelSize.Y / 2);
            
            // Background for label
            var bgMin = new Vector2(labelPos.X - 4, labelPos.Y - 2);
            var bgMax = new Vector2(p2.X, labelPos.Y + labelSize.Y + 2);
            
            // Clip to plot area so label goes under the axis
            drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.85f)), 2f);
            drawList.AddText(labelPos, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)), label);
            drawList.PopClipRect();
        }
    }
    
    /// <summary>
    /// Draws crosshair lines at the current mouse position.
    /// </summary>
    private static void DrawCrosshair(double mouseX, double mouseY, float valueAtMouse)
    {
        var drawList = ImPlot.GetPlotDrawList();
        var plotLimits = ImPlot.GetPlotLimits();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        
        var colorU32 = ImGui.GetColorU32(ChartColors.Crosshair);
        
        // Vertical line
        var vTop = ImPlot.PlotToPixels(mouseX, plotLimits.Y.Max);
        var vBottom = ImPlot.PlotToPixels(mouseX, plotLimits.Y.Min);
        
        // Draw dashed vertical line
        const float dashLength = 4f;
        const float gapLength = 3f;
        var y = vTop.Y;
        while (y < vBottom.Y)
        {
            var endY = Math.Min(y + dashLength, vBottom.Y);
            drawList.AddLine(new Vector2(vTop.X, y), new Vector2(vTop.X, endY), colorU32, 1f);
            y += dashLength + gapLength;
        }
        
        // Horizontal line
        var hLeft = ImPlot.PlotToPixels(plotLimits.X.Min, mouseY);
        var hRight = ImPlot.PlotToPixels(plotLimits.X.Max, mouseY);
        
        // Draw dashed horizontal line
        var x = hLeft.X;
        while (x < hRight.X)
        {
            var endX = Math.Min(x + dashLength, hRight.X);
            drawList.AddLine(new Vector2(x, hLeft.Y), new Vector2(endX, hLeft.Y), colorU32, 1f);
            x += dashLength + gapLength;
        }
        
        // Draw value label on Y axis (clipped to plot area so it goes under the Y-axis)
        var valueLabel = FormatUtils.FormatAbbreviated(valueAtMouse);
        var labelSize = ImGui.CalcTextSize(valueLabel);
        var labelPos = new Vector2(hRight.X - labelSize.X - 6, hRight.Y - labelSize.Y / 2);
        
        // Background box - clipped to plot area
        var bgPadding = 3f;
        drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
        drawList.AddRectFilled(
            new Vector2(labelPos.X - bgPadding, labelPos.Y - bgPadding),
            new Vector2(labelPos.X + labelSize.X + bgPadding, labelPos.Y + labelSize.Y + bgPadding),
            ImGui.GetColorU32(ChartColors.TooltipBackground), 2f);
        drawList.AddRect(
            new Vector2(labelPos.X - bgPadding, labelPos.Y - bgPadding),
            new Vector2(labelPos.X + labelSize.X + bgPadding, labelPos.Y + labelSize.Y + bgPadding),
            ImGui.GetColorU32(ChartColors.TooltipBorder), 2f);
        
        drawList.AddText(labelPos, ImGui.GetColorU32(ChartColors.TextPrimary), valueLabel);
        drawList.PopClipRect();
    }
    
    /// <summary>
    /// Draws a styled tooltip box at the given position.
    /// </summary>
    private static void DrawTooltipBox(Vector2 screenPos, string[] lines, Vector4 accentColor)
    {
        var drawList = ImPlot.GetPlotDrawList();
        
        // Calculate box size
        var maxWidth = 0f;
        var totalHeight = 0f;
        foreach (var line in lines)
        {
            var size = ImGui.CalcTextSize(line);
            maxWidth = Math.Max(maxWidth, size.X);
            totalHeight += size.Y + 2f;
        }
        
        var padding = 8f;
        var boxWidth = maxWidth + padding * 2 + 4; // +4 for accent bar
        var boxHeight = totalHeight + padding * 2 - 2f;
        
        // Offset to not overlap cursor
        var boxPos = new Vector2(screenPos.X + 12, screenPos.Y - boxHeight / 2);
        
        // Background
        drawList.AddRectFilled(
            boxPos,
            new Vector2(boxPos.X + boxWidth, boxPos.Y + boxHeight),
            ImGui.GetColorU32(ChartColors.TooltipBackground), 4f);
        
        // Border
        drawList.AddRect(
            boxPos,
            new Vector2(boxPos.X + boxWidth, boxPos.Y + boxHeight),
            ImGui.GetColorU32(ChartColors.TooltipBorder), 4f, 0, 1f);
        
        // Accent bar on left
        drawList.AddRectFilled(
            new Vector2(boxPos.X, boxPos.Y),
            new Vector2(boxPos.X + 3, boxPos.Y + boxHeight),
            ImGui.GetColorU32(accentColor), 4f);
        
        // Text
        var textY = boxPos.Y + padding;
        foreach (var line in lines)
        {
            drawList.AddText(new Vector2(boxPos.X + padding + 4, textY), ImGui.GetColorU32(ChartColors.TextPrimary), line);
            textY += ImGui.CalcTextSize(line).Y + 2f;
        }
    }


    /// <summary>
    /// Creates a new ImplotGraphWidget with default configuration.
    /// </summary>
    public ImplotGraphWidget() : this(new GraphConfig()) { }

    /// <summary>
    /// Creates a new ImplotGraphWidget with custom configuration.
    /// </summary>
    /// <param name="config">The graph configuration.</param>
    public ImplotGraphWidget(GraphConfig config)
    {
        _config = config ?? new GraphConfig();
    }

    /// <summary>
    /// Updates the Y-axis bounds without recreating the widget.
    /// </summary>
    /// <param name="minValue">New minimum value.</param>
    /// <param name="maxValue">New maximum value.</param>
    public void UpdateBounds(float minValue, float maxValue)
    {
        _config.MinValue = minValue;
        _config.MaxValue = maxValue;
    }

    /// <summary>
    /// Updates display options from external configuration.
    /// </summary>
    public void UpdateDisplayOptions(
        bool showValueLabel, 
        float valueLabelOffsetX = 0f, 
        float valueLabelOffsetY = 0f, 
        float legendWidth = 140f, 
        bool showLegend = true, 
        GraphType graphType = GraphType.Area, 
        bool showXAxisTimestamps = true,
        bool showCrosshair = true,
        bool showGridLines = true,
        bool showCurrentPriceLine = true,
        LegendPosition legendPosition = LegendPosition.InsideTopLeft,
        float legendHeightPercent = 25f,
        bool autoScrollEnabled = false,
        int autoScrollTimeValue = 1,
        TimeUnit autoScrollTimeUnit = TimeUnit.Hours,
        float autoScrollNowPosition = 75f,
        bool showControlsDrawer = true)
    {
        _config.ShowValueLabel = showValueLabel;
        _config.ValueLabelOffsetX = valueLabelOffsetX;
        _config.ValueLabelOffsetY = valueLabelOffsetY;
        _config.LegendWidth = legendWidth;
        _config.ShowLegend = showLegend;
        _config.GraphType = graphType;
        _config.ShowXAxisTimestamps = showXAxisTimestamps;
        _config.ShowCrosshair = showCrosshair;
        _config.ShowGridLines = showGridLines;
        _config.ShowCurrentPriceLine = showCurrentPriceLine;
        _config.LegendPosition = legendPosition;
        _config.LegendHeightPercent = legendHeightPercent;
        _config.AutoScrollEnabled = autoScrollEnabled;
        _config.AutoScrollTimeValue = autoScrollTimeValue;
        _config.AutoScrollTimeUnit = autoScrollTimeUnit;
        _config.AutoScrollNowPosition = autoScrollNowPosition;
        _config.ShowControlsDrawer = showControlsDrawer;
    }
    
    /// <summary>
    /// Gets or sets whether auto-scroll (follow mode) is enabled.
    /// </summary>
    public bool AutoScrollEnabled
    {
        get => _config.AutoScrollEnabled;
        set => _config.AutoScrollEnabled = value;
    }
    
    /// <summary>
    /// Gets or sets the auto-scroll time value.
    /// </summary>
    public int AutoScrollTimeValue
    {
        get => _config.AutoScrollTimeValue;
        set => _config.AutoScrollTimeValue = value;
    }
    
    /// <summary>
    /// Gets or sets the auto-scroll time unit.
    /// </summary>
    public TimeUnit AutoScrollTimeUnit
    {
        get => _config.AutoScrollTimeUnit;
        set => _config.AutoScrollTimeUnit = value;
    }

    /// <summary>
    /// Gets the current minimum Y-axis value.
    /// </summary>
    public float MinValue => _config.MinValue;

    /// <summary>
    /// Gets the current maximum Y-axis value.
    /// </summary>
    public float MaxValue => _config.MaxValue;

    #region ISettingsProvider Implementation
    
    /// <inheritdoc />
    public bool HasSettings => true;
    
    /// <inheritdoc />
    public string SettingsName => _settingsName;
    
    /// <summary>
    /// Binds the widget to an IGraphWidgetSettings object for automatic synchronization.
    /// When bound, the widget will read settings from this object and update it when settings change.
    /// </summary>
    /// <param name="settings">The settings object to bind to (must implement IGraphWidgetSettings).</param>
    /// <param name="onSettingsChanged">Optional callback invoked when a setting changes.</param>
    /// <param name="settingsName">Optional custom name for the settings section.</param>
    /// <param name="showLegendSettings">Whether to show legend settings (set false for single-series tools).</param>
    public void BindSettings(
        IGraphWidgetSettings settings,
        Action? onSettingsChanged = null,
        string? settingsName = null,
        bool showLegendSettings = true)
    {
        _boundSettings = settings;
        _onSettingsChanged = onSettingsChanged;
        if (settingsName != null) _settingsName = settingsName;
        _showLegendSettings = showLegendSettings;
        
        // Sync config from bound settings
        SyncFromBoundSettings();
    }
    
    /// <summary>
    /// Synchronizes the internal config from the bound settings object.
    /// Call this when the bound settings change externally.
    /// </summary>
    public void SyncFromBoundSettings()
    {
        if (_boundSettings == null) return;
        
        _config.LegendWidth = _boundSettings.LegendWidth;
        _config.LegendHeightPercent = _boundSettings.LegendHeightPercent;
        _config.ShowLegend = _boundSettings.ShowLegend;
        _config.LegendPosition = _boundSettings.LegendPosition;
        _config.GraphType = _boundSettings.GraphType;
        _config.ShowXAxisTimestamps = _boundSettings.ShowXAxisTimestamps;
        _config.ShowCrosshair = _boundSettings.ShowCrosshair;
        _config.ShowGridLines = _boundSettings.ShowGridLines;
        _config.ShowCurrentPriceLine = _boundSettings.ShowCurrentPriceLine;
        _config.ShowValueLabel = _boundSettings.ShowValueLabel;
        _config.ValueLabelOffsetX = _boundSettings.ValueLabelOffsetX;
        _config.ValueLabelOffsetY = _boundSettings.ValueLabelOffsetY;
        _config.AutoScrollEnabled = _boundSettings.AutoScrollEnabled;
        _config.AutoScrollTimeValue = _boundSettings.AutoScrollTimeValue;
        _config.AutoScrollTimeUnit = _boundSettings.AutoScrollTimeUnit;
        _config.AutoScrollNowPosition = _boundSettings.AutoScrollNowPosition;
        _config.ShowControlsDrawer = _boundSettings.ShowControlsDrawer;
    }
    
    /// <inheritdoc />
    public bool DrawSettings()
    {
        if (_boundSettings == null)
        {
            ImGui.TextDisabled("No settings bound to this graph widget.");
            return false;
        }
        
        var changed = false;
        var settings = _boundSettings;
        
        // Legend settings (only if enabled and multi-series)
        if (_showLegendSettings)
        {
            var showLegend = settings.ShowLegend;
            if (ImGui.Checkbox("Show legend", ref showLegend))
            {
                settings.ShowLegend = showLegend;
                _config.ShowLegend = showLegend;
                changed = true;
            }
            ShowSettingsTooltip("Shows a legend panel with series names and values.");
            
            if (showLegend)
            {
                var legendPosition = (int)settings.LegendPosition;
                if (ImGui.Combo("Legend position", ref legendPosition, LegendPositionNames, LegendPositionNames.Length))
                {
                    settings.LegendPosition = (LegendPosition)legendPosition;
                    _config.LegendPosition = settings.LegendPosition;
                    changed = true;
                }
                ShowSettingsTooltip("Where to display the legend: outside the graph or inside at a corner.");
                
                if (settings.LegendPosition == LegendPosition.Outside)
                {
                    var legendWidth = settings.LegendWidth;
                    if (ImGui.SliderFloat("Legend width", ref legendWidth, 60f, 250f, "%.0f px"))
                    {
                        settings.LegendWidth = legendWidth;
                        _config.LegendWidth = legendWidth;
                        changed = true;
                    }
                    ShowSettingsTooltip("Width of the scrollable legend panel.");
                }
                else
                {
                    var legendHeight = settings.LegendHeightPercent;
                    if (ImGui.SliderFloat("Legend height", ref legendHeight, 10f, 80f, "%.0f %%"))
                    {
                        settings.LegendHeightPercent = legendHeight;
                        _config.LegendHeightPercent = legendHeight;
                        changed = true;
                    }
                    ShowSettingsTooltip("Maximum height of the inside legend as a percentage of the graph height.");
                }
            }
            
            ImGui.Spacing();
        }
        
        // Value label settings
        var showValueLabel = settings.ShowValueLabel;
        if (ImGui.Checkbox("Show current value label", ref showValueLabel))
        {
            settings.ShowValueLabel = showValueLabel;
            _config.ShowValueLabel = showValueLabel;
            changed = true;
        }
        ShowSettingsTooltip("Shows the current value as a small label near the latest data point.");
        
        if (showValueLabel)
        {
            var labelOffsetX = settings.ValueLabelOffsetX;
            if (ImGui.SliderFloat("Label X offset", ref labelOffsetX, -100f, 100f, "%.0f"))
            {
                settings.ValueLabelOffsetX = labelOffsetX;
                _config.ValueLabelOffsetX = labelOffsetX;
                changed = true;
            }
            ShowSettingsTooltip("Horizontal offset for the value label. Negative = left, positive = right.");
            
            var labelOffsetY = settings.ValueLabelOffsetY;
            if (ImGui.SliderFloat("Label Y offset", ref labelOffsetY, -50f, 50f, "%.0f"))
            {
                settings.ValueLabelOffsetY = labelOffsetY;
                _config.ValueLabelOffsetY = labelOffsetY;
                changed = true;
            }
            ShowSettingsTooltip("Vertical offset for the value label. Negative = up, positive = down.");
        }
        
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Graph Style"))
        {
            // Graph type
            var graphType = settings.GraphType;
        if (GraphTypeSelectorWidget.Draw("Graph type", ref graphType))
        {
            settings.GraphType = graphType;
            _config.GraphType = graphType;
            changed = true;
        }
        ShowSettingsTooltip("The visual style for the graph (Area, Line, Stairs, Bars).");
        
        // X-axis timestamps
        var showXAxisTimestamps = settings.ShowXAxisTimestamps;
        if (ImGui.Checkbox("Show X-axis timestamps", ref showXAxisTimestamps))
        {
            settings.ShowXAxisTimestamps = showXAxisTimestamps;
            _config.ShowXAxisTimestamps = showXAxisTimestamps;
            changed = true;
        }
        ShowSettingsTooltip("Shows time labels on the X-axis.");
        
        // Crosshair
        var showCrosshair = settings.ShowCrosshair;
        if (ImGui.Checkbox("Show crosshair", ref showCrosshair))
        {
            settings.ShowCrosshair = showCrosshair;
            _config.ShowCrosshair = showCrosshair;
            changed = true;
        }
        ShowSettingsTooltip("Shows crosshair lines when hovering over the graph.");
        
        // Grid lines
        var showGridLines = settings.ShowGridLines;
        if (ImGui.Checkbox("Show grid lines", ref showGridLines))
        {
            settings.ShowGridLines = showGridLines;
            _config.ShowGridLines = showGridLines;
            changed = true;
        }
        ShowSettingsTooltip("Shows horizontal grid lines for easier value reading.");
        
        // Current price line
        var showCurrentPriceLine = settings.ShowCurrentPriceLine;
        if (ImGui.Checkbox("Show current price line", ref showCurrentPriceLine))
        {
            settings.ShowCurrentPriceLine = showCurrentPriceLine;
            _config.ShowCurrentPriceLine = showCurrentPriceLine;
            changed = true;
        }
            ShowSettingsTooltip("Shows a horizontal line at the current value.");
        }
        
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Auto-Scroll"))
        {
            // Controls drawer
            var showControlsDrawer = settings.ShowControlsDrawer;
        if (ImGui.Checkbox("Show controls drawer", ref showControlsDrawer))
        {
            settings.ShowControlsDrawer = showControlsDrawer;
            _config.ShowControlsDrawer = showControlsDrawer;
            changed = true;
        }
        ShowSettingsTooltip("Shows a small toggle button in the top-right corner to access auto-scroll controls.");
        
        // Auto-scroll enabled
        var autoScrollEnabled = settings.AutoScrollEnabled;
        if (ImGui.Checkbox("Auto-scroll enabled", ref autoScrollEnabled))
        {
            settings.AutoScrollEnabled = autoScrollEnabled;
            _config.AutoScrollEnabled = autoScrollEnabled;
            changed = true;
        }
        ShowSettingsTooltip("When enabled, the graph automatically scrolls to show the most recent data.");
        
        if (autoScrollEnabled)
        {
            ImGui.TextUnformatted("Auto-scroll time range:");
            ImGui.SetNextItemWidth(60);
            var timeValue = settings.AutoScrollTimeValue;
            if (ImGui.InputInt("##autoscroll_value", ref timeValue))
            {
                settings.AutoScrollTimeValue = Math.Clamp(timeValue, 1, 999);
                _config.AutoScrollTimeValue = settings.AutoScrollTimeValue;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var unitIndex = (int)settings.AutoScrollTimeUnit;
            if (ImGui.Combo("##autoscroll_unit", ref unitIndex, AutoScrollTimeUnitNames, AutoScrollTimeUnitNames.Length))
            {
                settings.AutoScrollTimeUnit = (TimeUnit)unitIndex;
                _config.AutoScrollTimeUnit = settings.AutoScrollTimeUnit;
                changed = true;
            }
            ShowSettingsTooltip("How much time to show when auto-scrolling.");
            
            var nowPosition = settings.AutoScrollNowPosition;
            if (ImGui.SliderFloat("Now position", ref nowPosition, 0f, 100f, "%.0f %%"))
            {
                settings.AutoScrollNowPosition = nowPosition;
                _config.AutoScrollNowPosition = nowPosition;
                changed = true;
            }
                ShowSettingsTooltip("Position of 'now' on the X-axis. 0% = left edge, 100% = right edge.");
            }
        }
        
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Time Range"))
        {
            var timeRangeValue = settings.TimeRangeValue;
        var timeRangeUnit = settings.TimeRangeUnit;
        if (TimeRangeSelectorWidget.DrawVertical(ref timeRangeValue, ref timeRangeUnit))
        {
            settings.TimeRangeValue = timeRangeValue;
                settings.TimeRangeUnit = timeRangeUnit;
                changed = true;
            }
            ShowSettingsTooltip("Time range to display on the graph.");
        }
        
        if (changed)
        {
            _onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Shows a tooltip for a setting if the item is hovered.
    /// </summary>
    private static void ShowSettingsTooltip(string description)
    {
        if (!ImGui.IsItemHovered()) return;
        
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(description);
        ImGui.EndTooltip();
    }
    
    #endregion

    #region Data Preparation

    /// <summary>
    /// Prepares index-based sample data for rendering.
    /// </summary>
    private PreparedGraphData PrepareIndexBasedData(IReadOnlyList<float> samples)
    {
        var xValues = new double[samples.Count];
        var yValues = new double[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            xValues[i] = i;
            yValues[i] = samples[i];
        }
        
        // Determine color based on trend
        var isBullish = samples.Count < 2 || samples[^1] >= samples[0];
        var color = isBullish ? new Vector3(ChartColors.Bullish.X, ChartColors.Bullish.Y, ChartColors.Bullish.Z)
                              : new Vector3(ChartColors.Bearish.X, ChartColors.Bearish.Y, ChartColors.Bearish.Z);
        
        var series = new List<GraphSeriesData>
        {
            new()
            {
                Name = "Value",
                XValues = xValues,
                YValues = yValues,
                PointCount = samples.Count,
                Color = color,
                Visible = true
            }
        };
        
        // Calculate bounds
        var (yMin, yMax) = CalculateYBounds(series, 0, double.MaxValue);
        var xDataMax = (double)samples.Count;
        var xPadding = Math.Max(xDataMax * 0.05, 1.0);
        
        return new PreparedGraphData
        {
            Series = series,
            XMin = 0,
            XMax = xDataMax + xPadding,
            YMin = yMin,
            YMax = yMax,
            IsTimeBased = false,
            StartTime = DateTime.MinValue,
            TotalTimeSpan = xDataMax
        };
    }
    
    /// <summary>
    /// Prepares time-based multi-series data for rendering.
    /// </summary>
    private PreparedGraphData PrepareTimeBasedData(
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> seriesData)
    {
        // Find global time range
        var globalMinTime = DateTime.MaxValue;
        var globalMaxTime = DateTime.UtcNow;
        
        foreach (var (_, samples) in seriesData)
        {
            if (samples == null || samples.Count == 0) continue;
            if (samples[0].ts < globalMinTime) globalMinTime = samples[0].ts;
        }
        
        if (globalMinTime == DateTime.MaxValue)
            globalMinTime = DateTime.UtcNow.AddHours(-1);
        
        var totalTimeSpan = (globalMaxTime - globalMinTime).TotalSeconds;
        if (totalTimeSpan < 1) totalTimeSpan = 1;
        
        // Generate colors
        var colors = GetSeriesColors(seriesData.Count);
        
        // Build series list using pooled arrays to avoid allocations
        var series = new List<GraphSeriesData>();
        for (var i = 0; i < seriesData.Count; i++)
        {
            var (name, samples) = seriesData[i];
            if (samples == null || samples.Count == 0) continue;
            
            // Get or create pooled arrays (reuse if size matches, otherwise allocate)
            var pointCount = samples.Count + 1;
            var xValues = GetOrCreatePooledArray(_pooledXArrays, name, pointCount);
            var yValues = GetOrCreatePooledArray(_pooledYArrays, name, pointCount);
            
            for (var j = 0; j < samples.Count; j++)
            {
                xValues[j] = (samples[j].ts - globalMinTime).TotalSeconds;
                yValues[j] = samples[j].value;
            }
            
            // Extend to current time with last value
            xValues[samples.Count] = totalTimeSpan;
            yValues[samples.Count] = samples[^1].value;
            
            series.Add(new GraphSeriesData
            {
                Name = name,
                XValues = xValues,
                YValues = yValues,
                PointCount = pointCount,
                Color = colors[i],
                Visible = !_hiddenSeries.Contains(name)
            });
        }
        
        // Calculate X limits based on auto-scroll mode
        double xMin, xMax;
        if (_config.AutoScrollEnabled)
        {
            var timeRangeSeconds = _config.GetAutoScrollTimeRangeSeconds();
            var nowFraction = _config.AutoScrollNowPosition / 100f;
            var leftPortion = timeRangeSeconds * nowFraction;
            var rightPortion = timeRangeSeconds * (1f - nowFraction);
            xMin = totalTimeSpan - leftPortion;
            xMax = totalTimeSpan + rightPortion;
        }
        else
        {
            xMin = 0;
            xMax = totalTimeSpan + Math.Max(totalTimeSpan * 0.05, 1.0);
        }
        
        // Calculate Y bounds for visible series (considering visible X range for auto-scroll)
        var (yMinCalc, yMaxCalc) = CalculateYBounds(series, xMin, xMax);
        
        return new PreparedGraphData
        {
            Series = series,
            XMin = xMin,
            XMax = xMax,
            YMin = yMinCalc,
            YMax = yMaxCalc,
            IsTimeBased = true,
            StartTime = globalMinTime,
            TotalTimeSpan = totalTimeSpan
        };
    }
    
    /// <summary>
    /// Gets a pooled array from the dictionary, or creates one if it doesn't exist or is too small.
    /// Arrays are reused across frames to avoid GC pressure.
    /// </summary>
    private static double[] GetOrCreatePooledArray(Dictionary<string, double[]> pool, string key, int requiredSize)
    {
        if (pool.TryGetValue(key, out var existing) && existing.Length >= requiredSize)
        {
            return existing;
        }
        
        // Allocate with some headroom to reduce reallocations for growing data
        var newArray = new double[requiredSize + 16];
        pool[key] = newArray;
        return newArray;
    }
    
    /// <summary>
    /// Calculates Y-axis bounds from visible series data.
    /// </summary>
    private (double yMin, double yMax) CalculateYBounds(
        IReadOnlyList<GraphSeriesData> series, 
        double xMinVisible, 
        double xMaxVisible)
    {
        var dataMin = double.MaxValue;
        var dataMax = double.MinValue;
        
        foreach (var s in series)
        {
            if (!s.Visible) continue;
            
            double? lastValueBeforeRange = null;
            
            for (var i = 0; i < s.PointCount; i++)
            {
                var x = s.XValues[i];
                var y = s.YValues[i];
                
                if (_config.AutoScrollEnabled)
                {
                    if (x < xMinVisible)
                    {
                        lastValueBeforeRange = y;
                        continue;
                    }
                    if (x > xMaxVisible) continue;
                }
                
                if (y < dataMin) dataMin = y;
                if (y > dataMax) dataMax = y;
            }
            
            // Include last value before visible range for proper bounds
            if (_config.AutoScrollEnabled && lastValueBeforeRange.HasValue)
            {
                if (lastValueBeforeRange.Value < dataMin) dataMin = lastValueBeforeRange.Value;
                if (lastValueBeforeRange.Value > dataMax) dataMax = lastValueBeforeRange.Value;
            }
        }
        
        if (dataMin == double.MaxValue || dataMax == double.MinValue)
        {
            dataMin = 0;
            dataMax = 100;
        }
        
        var dataRange = dataMax - dataMin;
        if (dataRange < _config.FloatEpsilon)
        {
            dataRange = Math.Max(dataMax * 0.1, 1.0);
        }
        
        var yMin = Math.Max(0, dataMin - dataRange * 0.15);
        var yMax = dataMax + dataRange * 0.15;
        
        if (Math.Abs(yMax - yMin) < _config.FloatEpsilon)
        {
            yMax = yMin + 1;
        }
        
        return (yMin, yMax);
    }

    #endregion

    #region Public Draw Methods

    /// <summary>
    /// Draws the graph with the provided samples using ImPlot (trading platform style).
    /// </summary>
    /// <param name="samples">The sample data to plot.</param>
    public void Draw(IReadOnlyList<float> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            DrawNoDataMessage();
            return;
        }

        PreparedGraphData preparedData;
        using (ProfilerService.BeginStaticChildScope("PrepareIndexData"))
        {
            preparedData = PrepareIndexBasedData(samples);
        }
        DrawGraph(preparedData);
    }

    /// <summary>
    /// Draws multiple data series overlaid on the same graph with time-aligned data.
    /// Uses caching to avoid recomputing prepared data every frame.
    /// </summary>
    /// <param name="series">List of data series with names and timestamped values.</param>
    public void DrawMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series)
    {
        if (series == null || series.Count == 0 || series.All(s => s.samples == null || s.samples.Count == 0))
        {
            DrawNoDataMessage();
            return;
        }

        PreparedGraphData data;
        
        // Check if we need to recompute prepared data
        bool needsRecompute;
        using (ProfilerService.BeginStaticChildScope("NeedsRecompute"))
        {
            needsRecompute = NeedsPreparedDataRecompute(series);
        }
        
        if (needsRecompute)
        {
            using (ProfilerService.BeginStaticChildScope("PrepareTimeData"))
            {
                data = PrepareTimeBasedData(series);
            }
            _cachedPreparedData = data;
            _lastSeriesData = series;
            _lastHiddenSeriesHash = ComputeHiddenSeriesHash();
            _lastAutoScrollEnabled = _config.AutoScrollEnabled;
            _lastPreparedDataTime = DateTime.UtcNow;
        }
        else
        {
            data = _cachedPreparedData!;
            
            // For auto-scroll, just update the X limits and Y bounds without reprocessing all data
            if (_config.AutoScrollEnabled)
            {
                using (ProfilerService.BeginStaticChildScope("UpdateAutoScroll"))
                {
                    UpdateAutoScrollLimits(data);
                }
            }
        }
        
        using (ProfilerService.BeginStaticChildScope("DrawGraph"))
        {
            DrawGraph(data);
        }
    }
    
    /// <summary>
    /// Draws multiple data series overlaid on the same graph with time-aligned data, using custom colors.
    /// Uses caching to avoid recomputing prepared data every frame.
    /// </summary>
    /// <param name="series">List of data series with names, timestamped values, and optional colors.</param>
    public void DrawMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> series)
    {
        if (series == null || series.Count == 0 || series.All(s => s.samples == null || s.samples.Count == 0))
        {
            DrawNoDataMessage();
            return;
        }

        PreparedGraphData data;
        
        // Check if we need to recompute prepared data
        bool needsRecompute;
        using (ProfilerService.BeginStaticChildScope("NeedsRecompute"))
        {
            needsRecompute = NeedsPreparedDataRecomputeWithColors(series);
        }
        
        if (needsRecompute)
        {
            using (ProfilerService.BeginStaticChildScope("PrepareTimeData"))
            {
                data = PrepareTimeBasedDataWithColors(series);
            }
            _cachedPreparedData = data;
            _lastSeriesDataWithColors = series;
            _lastSeriesData = null; // Clear the non-color version to avoid confusion
            _lastHiddenSeriesHash = ComputeHiddenSeriesHash();
            _lastAutoScrollEnabled = _config.AutoScrollEnabled;
            _lastPreparedDataTime = DateTime.UtcNow;
        }
        else
        {
            data = _cachedPreparedData!;
            
            // For auto-scroll, just update the X limits and Y bounds without reprocessing all data
            if (_config.AutoScrollEnabled)
            {
                using (ProfilerService.BeginStaticChildScope("UpdateAutoScroll"))
                {
                    UpdateAutoScrollLimits(data);
                }
            }
        }
        
        using (ProfilerService.BeginStaticChildScope("DrawGraph"))
        {
            DrawGraph(data);
        }
    }
    
    /// <summary>
    /// Determines if prepared graph data needs to be recomputed (for color-aware overload).
    /// </summary>
    private bool NeedsPreparedDataRecomputeWithColors(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> series)
    {
        // No cache yet
        if (_cachedPreparedData == null || _lastSeriesDataWithColors == null)
            return true;
        
        // Series reference changed (new data from caller)
        if (!ReferenceEquals(series, _lastSeriesDataWithColors))
            return true;
        
        // Hidden series changed
        var currentHiddenHash = ComputeHiddenSeriesHash();
        if (currentHiddenHash != _lastHiddenSeriesHash)
            return true;
        
        // Auto-scroll was just enabled/disabled
        if (_config.AutoScrollEnabled != _lastAutoScrollEnabled)
            return true;
        
        return false;
    }
    
    /// <summary>
    /// Prepares time-based multi-series data for rendering, using custom colors when provided.
    /// </summary>
    private PreparedGraphData PrepareTimeBasedDataWithColors(
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> seriesData)
    {
        // Find global time range
        var globalMinTime = DateTime.MaxValue;
        var globalMaxTime = DateTime.UtcNow;
        
        foreach (var (_, samples, _) in seriesData)
        {
            if (samples == null || samples.Count == 0) continue;
            if (samples[0].ts < globalMinTime) globalMinTime = samples[0].ts;
        }
        
        if (globalMinTime == DateTime.MaxValue)
            globalMinTime = DateTime.UtcNow.AddHours(-1);
        
        var totalTimeSpan = (globalMaxTime - globalMinTime).TotalSeconds;
        if (totalTimeSpan < 1) totalTimeSpan = 1;
        
        // Generate default colors for series without custom colors
        var defaultColors = GetSeriesColors(seriesData.Count);
        
        // Build series list using pooled arrays to avoid allocations
        var series = new List<GraphSeriesData>();
        var defaultColorIndex = 0;
        for (var i = 0; i < seriesData.Count; i++)
        {
            var (name, samples, customColor) = seriesData[i];
            if (samples == null || samples.Count == 0) continue;
            
            // Get or create pooled arrays (reuse if size matches, otherwise allocate)
            var pointCount = samples.Count + 1;
            var xValues = GetOrCreatePooledArray(_pooledXArrays, name, pointCount);
            var yValues = GetOrCreatePooledArray(_pooledYArrays, name, pointCount);
            
            for (var j = 0; j < samples.Count; j++)
            {
                xValues[j] = (samples[j].ts - globalMinTime).TotalSeconds;
                yValues[j] = samples[j].value;
            }
            
            // Extend to current time with last value
            xValues[samples.Count] = totalTimeSpan;
            yValues[samples.Count] = samples[^1].value;
            
            // Use custom color if provided, otherwise use default color palette
            var color = customColor.HasValue 
                ? new Vector3(customColor.Value.X, customColor.Value.Y, customColor.Value.Z)
                : defaultColors[defaultColorIndex++ % defaultColors.Length];
            
            series.Add(new GraphSeriesData
            {
                Name = name,
                XValues = xValues,
                YValues = yValues,
                PointCount = pointCount,
                Color = color,
                Visible = !_hiddenSeries.Contains(name)
            });
        }
        
        // Calculate X limits based on auto-scroll mode
        double xMin, xMax;
        if (_config.AutoScrollEnabled)
        {
            var timeRangeSeconds = _config.GetAutoScrollTimeRangeSeconds();
            var nowFraction = _config.AutoScrollNowPosition / 100f;
            var leftPortion = timeRangeSeconds * nowFraction;
            var rightPortion = timeRangeSeconds * (1f - nowFraction);
            xMin = totalTimeSpan - leftPortion;
            xMax = totalTimeSpan + rightPortion;
        }
        else
        {
            xMin = 0;
            xMax = totalTimeSpan + Math.Max(totalTimeSpan * 0.05, 1.0);
        }
        
        // Calculate Y bounds for visible series (considering visible X range for auto-scroll)
        var (yMinCalc, yMaxCalc) = CalculateYBounds(series, xMin, xMax);
        
        return new PreparedGraphData
        {
            Series = series,
            XMin = xMin,
            XMax = xMax,
            YMin = yMinCalc,
            YMax = yMaxCalc,
            IsTimeBased = true,
            StartTime = globalMinTime,
            TotalTimeSpan = totalTimeSpan
        };
    }
    
    /// <summary>
    /// Determines if prepared graph data needs to be recomputed.
    /// </summary>
    private bool NeedsPreparedDataRecompute(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series)
    {
        // No cache yet
        if (_cachedPreparedData == null || _lastSeriesData == null)
            return true;
        
        // Series reference changed (new data from caller)
        if (!ReferenceEquals(series, _lastSeriesData))
            return true;
        
        // Hidden series changed
        var currentHiddenHash = ComputeHiddenSeriesHash();
        if (currentHiddenHash != _lastHiddenSeriesHash)
            return true;
        
        // Auto-scroll was just enabled/disabled
        if (_config.AutoScrollEnabled != _lastAutoScrollEnabled)
            return true;
        
        // Note: For auto-scroll mode, we DON'T recompute here.
        // Instead, UpdateAutoScrollLimits() updates the bounds every frame.
        
        return false;
    }
    
    /// <summary>
    /// Computes a hash of the hidden series set for change detection.
    /// </summary>
    private int ComputeHiddenSeriesHash()
    {
        var hash = new HashCode();
        foreach (var name in _hiddenSeries.OrderBy(n => n))
        {
            hash.Add(name);
        }
        return hash.ToHashCode();
    }
    
    /// <summary>
    /// Updates just the X limits for auto-scroll mode without reprocessing data.
    /// This is called every frame when auto-scroll is enabled to keep "now" moving.
    /// </summary>
    private void UpdateAutoScrollLimits(PreparedGraphData data)
    {
        // Recalculate totalTimeSpan based on current time
        var globalMaxTime = DateTime.UtcNow;
        var totalTimeSpan = (globalMaxTime - data.StartTime).TotalSeconds;
        if (totalTimeSpan < 1) totalTimeSpan = 1;
        
        // Update the total time span in the data
        data.TotalTimeSpan = totalTimeSpan;
        
        // Update the extended point for each series to current time
        foreach (var series in data.Series)
        {
            if (series.PointCount > 0)
            {
                // The last point is the "extended to now" point - update its X value
                series.XValues[series.PointCount - 1] = totalTimeSpan;
            }
        }
        
        // Recalculate X limits based on auto-scroll settings
        var timeRangeSeconds = _config.GetAutoScrollTimeRangeSeconds();
        var nowFraction = _config.AutoScrollNowPosition / 100f;
        var leftPortion = timeRangeSeconds * nowFraction;
        var rightPortion = timeRangeSeconds * (1f - nowFraction);
        
        data.XMin = totalTimeSpan - leftPortion;
        data.XMax = totalTimeSpan + rightPortion;
        
        // Recalculate Y bounds for the visible X range
        var (yMin, yMax) = CalculateYBounds(data.Series, data.XMin, data.XMax);
        data.YMin = yMin;
        data.YMax = yMax;
    }
    
    /// <summary>
    /// Draws the graph using virtualized data loading.
    /// Only loads data within the visible time window, requesting more as the user scrolls.
    /// Requires DataProvider to be set.
    /// </summary>
    public void DrawVirtualized()
    {
        if (DataProvider == null)
        {
            DrawNoDataMessage();
            return;
        }
        
        // Calculate the visible time window
        var now = DateTime.UtcNow;
        DateTime windowStart, windowEnd;
        
        if (_config.AutoScrollEnabled)
        {
            // In auto-scroll mode, calculate window from settings
            var timeRangeSeconds = _config.GetAutoScrollTimeRangeSeconds();
            var nowFraction = _config.AutoScrollNowPosition / 100f;
            var leftSeconds = timeRangeSeconds * nowFraction * LoadBufferFactor;
            var rightSeconds = timeRangeSeconds * (1f - nowFraction) * LoadBufferFactor;
            
            windowStart = now.AddSeconds(-leftSeconds);
            windowEnd = now.AddSeconds(rightSeconds);
        }
        else
        {
            // Not in auto-scroll - use available data range or default
            if (_availableDataRange.HasValue)
            {
                windowStart = _availableDataRange.Value.earliest;
                windowEnd = now;
            }
            else
            {
                // Default to last 24 hours if no range info
                windowStart = now.AddHours(-24);
                windowEnd = now;
            }
        }
        
        // Check if we need to load new data (current data doesn't cover the window)
        var needsNewData = !_loadedDataRange.HasValue ||
                           windowStart < _loadedDataRange.Value.start ||
                           windowEnd > _loadedDataRange.Value.end;
        
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? series = null;
        
        if (needsNewData)
        {
            // Request data with buffer
            using (ProfilerService.BeginStaticChildScope("LoadVirtualData"))
            {
                series = DataProvider(windowStart, windowEnd);
            }
            if (series != null && series.Count > 0)
            {
                _loadedDataRange = (windowStart, windowEnd);
            }
        }
        
        // If we have cached data and don't need new data, we still need to draw
        // But we need the series data - for now, always request (the provider should cache)
        if (series == null)
        {
            using (ProfilerService.BeginStaticChildScope("LoadVirtualData"))
            {
                series = DataProvider(windowStart, windowEnd);
            }
        }
        
        if (series == null || series.Count == 0 || series.All(s => s.samples == null || s.samples.Count == 0))
        {
            DrawNoDataMessage();
            return;
        }
        
        PreparedGraphData data;
        using (ProfilerService.BeginStaticChildScope("PrepareTimeData"))
        {
            data = PrepareTimeBasedData(series);
        }
        DrawGraph(data);
    }
    
    /// <summary>
    /// Displays the "no data" message.
    /// </summary>
    private void DrawNoDataMessage()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ChartColors.TextSecondary);
        ImGui.TextUnformatted(_config.NoDataText);
        ImGui.PopStyleColor();
    }

    #endregion

    #region Unified Graph Rendering

    /// <summary>
    /// Core graph drawing method used by both Draw() and DrawMultipleSeries().
    /// </summary>
    private unsafe void DrawGraph(PreparedGraphData data)
    {
        try
        {
            var avail = ImGui.GetContentRegionAvail();
            
            // Reserve space for outside legend if needed (use total series count so legend stays visible when series are hidden)
            var useOutsideLegend = _config.ShowLegend && 
                                   _config.LegendPosition == LegendPosition.Outside && 
                                   data.HasMultipleSeriesTotal;
            var legendWidth = useOutsideLegend ? _config.LegendWidth : 0f;
            var legendPadding = useOutsideLegend ? 5f : 0f;
            var plotWidth = Math.Max(1f, avail.X - legendWidth - legendPadding);
            var plotSize = new Vector2(plotWidth, Math.Max(1f, avail.Y));

            // Configure plot flags
            var plotFlags = ImPlotFlags.NoTitle | ImPlotFlags.NoLegend | ImPlotFlags.NoMenus | 
                           ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText | ImPlotFlags.Crosshairs;
            
            if (IsMouseOverOverlay)
            {
                plotFlags |= ImPlotFlags.NoInputs;
            }
            
            // Apply styling
            PushChartStyle();
            
            // Set axis limits
            var plotCondition = _config.AutoScrollEnabled ? ImPlotCond.Always : ImPlotCond.Once;
            ImPlot.SetNextAxesLimits(data.XMin, data.XMax, data.YMin, data.YMax, plotCondition);

            var plotId = data.HasMultipleSeries ? $"##{_config.PlotId}_multi" : $"##{_config.PlotId}";
            
            if (ImPlot.BeginPlot(plotId, plotSize, plotFlags))
            {
                // Configure axes
                var xAxisFlags = data.IsTimeBased && _config.ShowXAxisTimestamps 
                    ? ImPlotAxisFlags.None 
                    : ImPlotAxisFlags.NoTickLabels;
                var yAxisFlags = ImPlotAxisFlags.Opposite;
                
                if (!_config.ShowGridLines)
                {
                    xAxisFlags |= ImPlotAxisFlags.NoGridLines;
                    yAxisFlags |= ImPlotAxisFlags.NoGridLines;
                }
                
                ImPlot.SetupAxes("", "", xAxisFlags, yAxisFlags);
                
                // Format axes
                if (data.IsTimeBased && _config.ShowXAxisTimestamps)
                {
                    ImPlot.SetupAxisFormat(ImAxis.X1, XAxisTimeFormatter, (void*)data.StartTime.Ticks);
                }
                ImPlot.SetupAxisFormat(ImAxis.Y1, YAxisFormatter);
                
                // Constrain axes
                ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, 0, double.MaxValue);
                ImPlot.SetupAxisLimitsConstraints(ImAxis.Y1, 0, double.MaxValue);
                
                // Plot dummy points for auto-fit padding
                var dummyX = stackalloc double[2] { 0, data.XMax };
                var dummyY = stackalloc double[2] { data.YMin > 0 ? data.YMin : 0, data.YMax };
                ImPlot.SetNextMarkerStyle(ImPlotMarker.None);
                ImPlot.SetNextLineStyle(new Vector4(0, 0, 0, 0), 0);
                ImPlot.PlotLine("##padding", dummyX, dummyY, 2);
                
                // Draw each series
                using (ProfilerService.BeginStaticChildScope("DrawSeries"))
                {
                    foreach (var series in data.Series)
                    {
                        if (!series.Visible) continue;
                        DrawSeries(series, data);
                    }
                }
                
                // Draw current price line for the last visible series (only for single series)
                if (_config.ShowCurrentPriceLine && !data.HasMultipleSeries)
                {
                    var lastVisibleSeries = data.Series.LastOrDefault(s => s.Visible);
                    if (lastVisibleSeries != null && lastVisibleSeries.PointCount > 0)
                    {
                        var currentValue = (float)lastVisibleSeries.YValues[lastVisibleSeries.PointCount - 1];
                        var isBullish = lastVisibleSeries.PointCount < 2 || 
                                       lastVisibleSeries.YValues[lastVisibleSeries.PointCount - 1] >= lastVisibleSeries.YValues[0];
                        var priceLineColor = isBullish ? ChartColors.Bullish : ChartColors.Bearish;
                        DrawPriceLine(currentValue, FormatUtils.FormatAbbreviated(currentValue), priceLineColor, 1.5f, true);
                    }
                }
                
                // Draw hover effects
                if (ImPlot.IsPlotHovered())
                {
                    using (ProfilerService.BeginStaticChildScope("DrawHoverEffects"))
                    {
                        DrawHoverEffects(data);
                    }
                }
                
                // Draw inside legend if applicable (use total series count so legend stays visible when series are hidden)
                // Draw this BEFORE value labels so bounds are cached for overlap detection
                if (_config.ShowLegend && _config.LegendPosition != LegendPosition.Outside && data.HasMultipleSeriesTotal)
                {
                    using (ProfilerService.BeginStaticChildScope("DrawInsideLegend"))
                    {
                        DrawInsideLegend(data);
                    }
                }
                else
                {
                    _cachedLegendBounds = (Vector2.Zero, Vector2.Zero, false);
                }
                
                // Draw controls drawer
                // Draw this BEFORE value labels so bounds are cached for overlap detection
                if (_config.ShowControlsDrawer)
                {
                    DrawControlsDrawer();
                }
                else
                {
                    _cachedControlsDrawerBounds = (Vector2.Zero, Vector2.Zero, false);
                }
                
                // Draw value labels AFTER legend and drawer so we can check for overlap
                if (_config.ShowValueLabel)
                {
                    DrawValueLabels(data);
                }
                
                ImPlot.EndPlot();
            }
            
            PopChartStyle();
            
            // Draw outside legend
            if (useOutsideLegend)
            {
                ImGui.SameLine();
                using (ProfilerService.BeginStaticChildScope("DrawOutsideLegend"))
                {
                    DrawScrollableLegend(data, _config.LegendWidth, avail.Y);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ImplotGraphWidget] Graph rendering error: {ex.Message}");
            ImGui.TextUnformatted("Error rendering graph");
        }
    }
    
    /// <summary>
    /// Draws a single series on the plot.
    /// </summary>
    private unsafe void DrawSeries(GraphSeriesData series, PreparedGraphData data)
    {
        var colorVec4 = new Vector4(series.Color.X, series.Color.Y, series.Color.Z, 1f);
        ImPlot.SetNextLineStyle(colorVec4, 2f);
        
        fixed (double* xPtr = series.XValues)
        fixed (double* yPtr = series.YValues)
        {
            var count = series.PointCount;
            
            switch (_config.GraphType)
            {
                case GraphType.Line:
                    ImPlot.PlotLine(series.Name, xPtr, yPtr, count);
                    break;
                    
                case GraphType.Stairs:
                    ImPlot.PlotStairs(series.Name, xPtr, yPtr, count);
                    break;
                    
                case GraphType.Bars:
                    var barWidth = data.HasMultipleSeries 
                        ? data.TotalTimeSpan / count * 0.8 / data.Series.Count(s => s.Visible)
                        : 0.67;
                    ImPlot.SetNextFillStyle(colorVec4);
                    ImPlot.PlotBars(series.Name, xPtr, yPtr, count, barWidth);
                    break;
                    
                case GraphType.Area:
                default:
                    var fillAlpha = data.HasMultipleSeries ? 0.55f : 0.60f;
                    ImPlot.SetNextFillStyle(new Vector4(series.Color.X, series.Color.Y, series.Color.Z, fillAlpha));
                    ImPlot.PlotShaded($"{series.Name}##shaded", xPtr, yPtr, count, 0.0);
                    ImPlot.SetNextLineStyle(colorVec4, 2f);
                    ImPlot.PlotLine(series.Name, xPtr, yPtr, count);
                    break;
            }
        }
    }
    
    /// <summary>
    /// Draws crosshair and tooltip when hovering over the plot.
    /// Uses binary search for time-based data to avoid O(n) linear scan.
    /// </summary>
    private void DrawHoverEffects(PreparedGraphData data)
    {
        var mousePos = ImPlot.GetPlotMousePos();
        var mouseX = mousePos.X;
        var mouseY = mousePos.Y;
        
        // Find nearest point across all visible series
        string nearestSeriesName = string.Empty;
        float nearestValue = 0f;
        var nearestColor = new Vector3(1f, 1f, 1f);
        var minYDistance = double.MaxValue;
        var foundPoint = false;
        var pointX = 0.0;
        var pointY = 0.0;
        
        foreach (var series in data.Series)
        {
            if (!series.Visible || series.PointCount == 0) continue;
            
            // Use binary search for time-based data (X values are sorted)
            var idx = BinarySearchNearestX(series.XValues, series.PointCount, mouseX);
            
            if (idx >= 0 && idx < series.PointCount)
            {
                var value = (float)series.YValues[idx];
                var yDistance = Math.Abs(mouseY - value);
                
                if (yDistance < minYDistance)
                {
                    minYDistance = yDistance;
                    nearestSeriesName = series.Name;
                    nearestValue = value;
                    nearestColor = series.Color;
                    pointX = mouseX;
                    pointY = value;
                    foundPoint = true;
                }
            }
        }
        
        if (foundPoint)
        {
            if (_config.ShowCrosshair)
            {
                DrawCrosshair(pointX, pointY, nearestValue);
            }
            
            var screenPos = ImPlot.PlotToPixels(pointX, pointY);
            var accentColor = new Vector4(nearestColor.X, nearestColor.Y, nearestColor.Z, 1f);
            
            var tooltipLines = data.HasMultipleSeries
                ? new[] { nearestSeriesName, $"Value: {FormatValue(nearestValue)}" }
                : new[] { $"Value: {FormatValue(nearestValue)}" };
            
            DrawTooltipBox(screenPos, tooltipLines, accentColor);
        }
    }
    
    /// <summary>
    /// Draws value labels at the end of each visible series.
    /// Uses cached label list to avoid LINQ allocations every frame.
    /// </summary>
    private void DrawValueLabels(PreparedGraphData data)
    {
        var drawList = ImPlot.GetPlotDrawList();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        
        // Clip to plot area so labels go under the Y-axis
        drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
        
        // Collect labels from visible series using cached list to avoid allocations
        _valueLabelCache.Clear();
        foreach (var s in data.Series)
        {
            if (s.Visible && s.PointCount > 0)
            {
                _valueLabelCache.Add((s.Name, (float)s.YValues[s.PointCount - 1], s.Color));
            }
        }
        
        // Sort by value ascending so higher values are drawn last (on top when overlapping)
        _valueLabelCache.Sort((a, b) => a.Value.CompareTo(b.Value));
        
        const float padding = 4f;
        const float rounding = 3f;
        
        for (var i = 0; i < _valueLabelCache.Count; i++)
        {
            var (name, lastValue, color) = _valueLabelCache[i];
            var text = data.HasMultipleSeries 
                ? $"{name}: {FormatValue(lastValue)}"
                : FormatValue(lastValue);
            
            var xPos = data.IsTimeBased ? data.TotalTimeSpan : data.XMax - (data.XMax - data.XMin) * 0.05;
            var dataPointScreenPos = ImPlot.PlotToPixels(xPos, lastValue);
            
            // Calculate text size first for centering calculation
            var textSize = ImGui.CalcTextSize(text);
            
            // Calculate offset magnitude to determine if we should center the label
            var offsetMagnitudeForCentering = MathF.Sqrt(_config.ValueLabelOffsetX * _config.ValueLabelOffsetX + 
                                                          _config.ValueLabelOffsetY * _config.ValueLabelOffsetY);
            
            // Label position with offset - center on data point when offset is near zero
            var labelPos = new Vector2(
                dataPointScreenPos.X + _config.ValueLabelOffsetX - (offsetMagnitudeForCentering < 1f ? textSize.X / 2 : 0f),
                dataPointScreenPos.Y + _config.ValueLabelOffsetY);
            
            // Skip if label would overlap with legend or controls drawer
            var labelMin = new Vector2(labelPos.X - padding, labelPos.Y - textSize.Y / 2 - padding);
            var labelMax = new Vector2(labelPos.X + textSize.X + padding, labelPos.Y + textSize.Y / 2 + padding);
            
            if (IsPointInLegend(labelPos) || IsPointInControlsDrawer(labelPos) ||
                IsPointInLegend(labelMin) || IsPointInControlsDrawer(labelMin) ||
                IsPointInLegend(labelMax) || IsPointInControlsDrawer(labelMax))
            {
                continue;
            }
            
            var labelColor = new Vector4(color.X, color.Y, color.Z, 0.9f);
            var bgColor = ImGui.GetColorU32(labelColor);
            var lineColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.7f));
            var textColor = ImGui.GetColorU32(ChartColors.TextPrimary);
            
            // Draw connecting line from data point to label (if offset is significant)
            if (offsetMagnitudeForCentering > 5f)
            {
                // Line connects to the left edge of the label box
                var lineEnd = new Vector2(labelMin.X, labelPos.Y);
                drawList.AddLine(dataPointScreenPos, lineEnd, lineColor, 1.5f);
            }
            
            // Draw background box
            drawList.AddRectFilled(labelMin, labelMax, bgColor, rounding);
            drawList.AddText(new Vector2(labelPos.X, labelPos.Y - textSize.Y / 2), textColor, text);
        }
        
        drawList.PopClipRect();
    }

    #endregion

    #region Legend Drawing
    
    /// <summary>
    /// Draws a scrollable legend for multi-series graphs with trading platform styling.
    /// Uses cached sorted list to avoid LINQ allocations every frame.
    /// </summary>
    private void DrawScrollableLegend(PreparedGraphData data, float width, float height)
    {
        // Style the legend panel with trading platform colors
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ChartColors.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, ChartColors.AxisLine);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, ChartColors.PlotBackground);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, ChartColors.GridLine);
        
        if (ImGui.BeginChild($"##{_config.PlotId}_legend", new Vector2(width, height), true))
        {
            // Update cached sorted list only when series changes
            UpdateSortedSeriesCache(data);
            
            foreach (var series in _sortedSeriesCache)
            {
                var isHidden = !series.Visible;
                var lastValue = series.PointCount > 0 ? (float)series.YValues[series.PointCount - 1] : 0f;
                
                // Use dimmed color for hidden series
                var displayAlpha = isHidden ? 0.35f : 1f;
                
                // Draw colored square indicator (rounded for modern look)
                var drawList = ImGui.GetWindowDrawList();
                var cursorPos = ImGui.GetCursorScreenPos();
                const float indicatorSize = 10f;
                var colorU32 = ImGui.GetColorU32(new Vector4(series.Color.X, series.Color.Y, series.Color.Z, displayAlpha));
                
                if (isHidden)
                {
                    // Draw outline only for hidden series
                    drawList.AddRect(cursorPos, new Vector2(cursorPos.X + indicatorSize, cursorPos.Y + indicatorSize), colorU32, 2f);
                }
                else
                {
                    // Draw filled rounded square for visible series
                    drawList.AddRectFilled(cursorPos, new Vector2(cursorPos.X + indicatorSize, cursorPos.Y + indicatorSize), colorU32, 2f);
                }
                
                // Make the entire row clickable
                var rowStart = cursorPos;
                
                // Advance cursor past the indicator
                ImGui.Dummy(new Vector2(indicatorSize + 4f, indicatorSize));
                ImGui.SameLine();
                
                // Draw name with appropriate text color
                var textColor = isHidden ? ChartColors.TextSecondary : ChartColors.TextPrimary;
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.TextUnformatted($"{series.Name}");
                ImGui.PopStyleColor();
                
                // Make row clickable - use invisible button over the row area
                var rowEnd = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(rowStart);
                if (ImGui.InvisibleButton($"##legend_toggle_{series.Name}", new Vector2(width - 16f, indicatorSize + 2f)))
                {
                    ToggleSeriesVisibility(series.Name);
                }
                
                // Show tooltip on hover with styled content
                if (ImGui.IsItemHovered())
                {
                    var statusText = isHidden ? " (hidden)" : "";
                    ImGui.SetTooltip($"{series.Name}: {FormatValue(lastValue)}{statusText}\nClick to toggle visibility");
                }
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(4);
    }
    
    /// <summary>
    /// Draws an interactive legend inside the plot area using ImPlot's draw list.
    /// The legend is positioned based on the LegendPosition config setting.
    /// Supports scrolling when there are too many series.
    /// </summary>
    private void DrawInsideLegend(PreparedGraphData data)
    {
        var drawList = ImPlot.GetPlotDrawList();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        
        // Calculate legend dimensions
        const float padding = 8f;
        const float indicatorSize = 10f;
        const float rowHeight = 18f;
        const float indicatorTextGap = 6f;
        const float scrollbarWidth = 6f;
        
        // Measure max text width and count valid series
        var maxTextWidth = 0f;
        var validSeriesCount = 0;
        foreach (var series in data.Series)
        {
            var textSize = ImGui.CalcTextSize(series.Name);
            maxTextWidth = Math.Max(maxTextWidth, textSize.X);
            validSeriesCount++;
        }
        
        if (validSeriesCount == 0) return;
        
        // Calculate content height and max display height
        var contentHeight = validSeriesCount * rowHeight;
        var maxLegendHeight = plotSize.Y * (_config.LegendHeightPercent / 100f);
        maxLegendHeight = Math.Max(maxLegendHeight, rowHeight + padding * 2);
        var needsScrolling = contentHeight > maxLegendHeight - padding * 2;
        
        var legendWidth = padding * 2 + indicatorSize + indicatorTextGap + maxTextWidth + (needsScrolling ? scrollbarWidth + 4f : 0f);
        var legendHeight = Math.Min(padding * 2 + contentHeight, maxLegendHeight);
        
        // Determine legend position
        Vector2 legendPos;
        switch (_config.LegendPosition)
        {
            case LegendPosition.InsideTopRight:
                legendPos = new Vector2(plotPos.X + plotSize.X - legendWidth - 10, plotPos.Y + 10);
                break;
            case LegendPosition.InsideBottomLeft:
                legendPos = new Vector2(plotPos.X + 10, plotPos.Y + plotSize.Y - legendHeight - 10);
                break;
            case LegendPosition.InsideBottomRight:
                legendPos = new Vector2(plotPos.X + plotSize.X - legendWidth - 10, plotPos.Y + plotSize.Y - legendHeight - 10);
                break;
            case LegendPosition.InsideTopLeft:
            default:
                legendPos = new Vector2(plotPos.X + 10, plotPos.Y + 10);
                break;
        }
        
        // Cache legend bounds
        _cachedLegendBounds = (legendPos, new Vector2(legendPos.X + legendWidth, legendPos.Y + legendHeight), true);
        
        // Draw legend background
        var bgColor = ImGui.GetColorU32(new Vector4(ChartColors.FrameBackground.X, ChartColors.FrameBackground.Y, ChartColors.FrameBackground.Z, 0.85f));
        var borderColor = ImGui.GetColorU32(ChartColors.AxisLine);
        drawList.AddRectFilled(legendPos, new Vector2(legendPos.X + legendWidth, legendPos.Y + legendHeight), bgColor, 4f);
        drawList.AddRect(legendPos, new Vector2(legendPos.X + legendWidth, legendPos.Y + legendHeight), borderColor, 4f);
        
        // Track mouse interactions
        var mousePos = ImGui.GetMousePos();
        var mouseInLegend = mousePos.X >= legendPos.X && mousePos.X <= legendPos.X + legendWidth &&
                           mousePos.Y >= legendPos.Y && mousePos.Y <= legendPos.Y + legendHeight;
        
        // Handle mouse wheel scrolling
        if (mouseInLegend && needsScrolling)
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0)
            {
                _insideLegendScrollOffset -= wheel * rowHeight * 2f;
            }
        }
        
        // Clamp scroll offset
        var maxScrollOffset = Math.Max(0f, contentHeight - (legendHeight - padding * 2));
        _insideLegendScrollOffset = Math.Clamp(_insideLegendScrollOffset, 0f, maxScrollOffset);
        
        // Update cached sorted list only when series changes
        UpdateSortedSeriesCache(data);
        
        // Calculate visible area
        var contentAreaTop = legendPos.Y + padding;
        var contentAreaBottom = legendPos.Y + legendHeight - padding;
        var contentAreaRight = legendPos.X + legendWidth - padding - (needsScrolling ? scrollbarWidth + 4f : 0f);
        
        // Draw each legend entry
        var yOffset = contentAreaTop - _insideLegendScrollOffset;
        foreach (var series in _sortedSeriesCache)
        {
            var rowTop = yOffset;
            var rowBottom = yOffset + rowHeight;
            
            // Skip rows outside visible area
            if (rowBottom < contentAreaTop || rowTop > contentAreaBottom)
            {
                yOffset += rowHeight;
                continue;
            }
            
            var isHidden = !series.Visible;
            var displayAlpha = isHidden ? 0.35f : 1f;
            
            // Check if mouse is over this row
            var mouseInRow = mouseInLegend && 
                            mousePos.Y >= Math.Max(rowTop, contentAreaTop) && 
                            mousePos.Y < Math.Min(rowBottom, contentAreaBottom) &&
                            rowTop >= contentAreaTop && rowBottom <= contentAreaBottom;
            
            // Handle click to toggle visibility
            if (mouseInRow && ImGui.IsMouseClicked(0))
            {
                ToggleSeriesVisibility(series.Name);
            }
            
            // Highlight row on hover
            if (mouseInRow)
            {
                var hoverColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.1f));
                drawList.AddRectFilled(
                    new Vector2(legendPos.X + 2, Math.Max(rowTop, contentAreaTop)), 
                    new Vector2(contentAreaRight, Math.Min(rowBottom, contentAreaBottom)), 
                    hoverColor, 2f);
            }
            
            // Draw content if visible
            if (rowTop >= contentAreaTop - rowHeight && rowBottom <= contentAreaBottom + rowHeight)
            {
                var indicatorY = yOffset + (rowHeight - indicatorSize) / 2;
                if (indicatorY >= contentAreaTop - indicatorSize && indicatorY + indicatorSize <= contentAreaBottom + indicatorSize)
                {
                    var indicatorPos = new Vector2(legendPos.X + padding, indicatorY);
                    var colorU32 = ImGui.GetColorU32(new Vector4(series.Color.X, series.Color.Y, series.Color.Z, displayAlpha));
                    
                    if (isHidden)
                    {
                        drawList.AddRect(indicatorPos, new Vector2(indicatorPos.X + indicatorSize, indicatorPos.Y + indicatorSize), colorU32, 2f);
                    }
                    else
                    {
                        drawList.AddRectFilled(indicatorPos, new Vector2(indicatorPos.X + indicatorSize, indicatorPos.Y + indicatorSize), colorU32, 2f);
                    }
                    
                    // Draw series name
                    var textColor = isHidden ? ChartColors.TextSecondary : ChartColors.TextPrimary;
                    var textY = yOffset + (rowHeight - ImGui.GetTextLineHeight()) / 2;
                    if (textY >= contentAreaTop - rowHeight && textY <= contentAreaBottom)
                    {
                        var textPos = new Vector2(indicatorPos.X + indicatorSize + indicatorTextGap, textY);
                        drawList.AddText(textPos, ImGui.GetColorU32(textColor), series.Name);
                    }
                }
            }
            
            yOffset += rowHeight;
        }
        
        // Draw scrollbar if needed
        if (needsScrolling)
        {
            var scrollTrackTop = legendPos.Y + padding;
            var scrollTrackBottom = legendPos.Y + legendHeight - padding;
            var scrollTrackHeight = scrollTrackBottom - scrollTrackTop;
            var scrollTrackX = legendPos.X + legendWidth - padding - scrollbarWidth;
            
            var trackColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
            drawList.AddRectFilled(
                new Vector2(scrollTrackX, scrollTrackTop),
                new Vector2(scrollTrackX + scrollbarWidth, scrollTrackBottom),
                trackColor, 3f);
            
            var visibleRatio = (legendHeight - padding * 2) / contentHeight;
            var thumbHeight = Math.Max(20f, scrollTrackHeight * visibleRatio);
            var scrollRatio = maxScrollOffset > 0 ? _insideLegendScrollOffset / maxScrollOffset : 0f;
            var thumbTop = scrollTrackTop + scrollRatio * (scrollTrackHeight - thumbHeight);
            
            var thumbColor = ImGui.GetColorU32(ChartColors.GridLine);
            drawList.AddRectFilled(
                new Vector2(scrollTrackX, thumbTop),
                new Vector2(scrollTrackX + scrollbarWidth, thumbTop + thumbHeight),
                thumbColor, 3f);
        }
        
        // Show tooltip
        if (mouseInLegend)
        {
            var relativeY = mousePos.Y - contentAreaTop + _insideLegendScrollOffset;
            var hoveredIdx = (int)(relativeY / rowHeight);
            if (hoveredIdx >= 0 && hoveredIdx < _sortedSeriesCache.Count)
            {
                var rowTop = contentAreaTop - _insideLegendScrollOffset + hoveredIdx * rowHeight;
                var rowBottom = rowTop + rowHeight;
                if (rowTop < contentAreaBottom && rowBottom > contentAreaTop)
                {
                    var series = _sortedSeriesCache[hoveredIdx];
                    var lastValue = series.PointCount > 0 ? (float)series.YValues[series.PointCount - 1] : 0f;
                    var statusText = !series.Visible ? " (hidden)" : "";
                    var scrollHint = needsScrolling ? "\nScroll to see more" : "";
                    ImGui.SetTooltip($"{series.Name}: {FormatValue(lastValue)}{statusText}\nClick to toggle visibility{scrollHint}");
                }
            }
        }
    }

    #endregion

    #region Controls Drawer

    /// <summary>
    /// Draws the controls drawer with toggle button and auto-scroll controls.
    /// The drawer slides out from the top-right corner of the plot.
    /// </summary>
    private void DrawControlsDrawer()
    {
        var drawList = ImPlot.GetPlotDrawList();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        
        // Constants for drawer layout
        const float toggleButtonWidth = 24f;
        const float toggleButtonHeight = 20f;
        const float drawerWidth = 160f;
        const float drawerPadding = 8f;
        const float rowHeight = 22f;
        const float checkboxSize = 14f;
        
        // Calculate drawer height based on content
        var drawerContentHeight = rowHeight; // Auto-scroll checkbox
        if (_config.AutoScrollEnabled)
        {
            drawerContentHeight += rowHeight; // Value input row
            drawerContentHeight += rowHeight; // Unit selector row
            drawerContentHeight += rowHeight; // Position slider row
        }
        var drawerHeight = drawerPadding * 2 + drawerContentHeight;
        
        // Position toggle button at top-right corner of plot
        var toggleButtonPos = new Vector2(
            plotPos.X + plotSize.X - toggleButtonWidth - 10,
            plotPos.Y + 10
        );
        
        // Position drawer below the toggle button
        var drawerPos = new Vector2(
            toggleButtonPos.X - drawerWidth - 4 + toggleButtonWidth,
            toggleButtonPos.Y + toggleButtonHeight + 4
        );
        
        // Track mouse interactions
        var mousePos = ImGui.GetMousePos();
        
        // Draw toggle button
        var buttonBgColor = ImGui.GetColorU32(new Vector4(ChartColors.FrameBackground.X, ChartColors.FrameBackground.Y, ChartColors.FrameBackground.Z, 0.9f));
        var buttonBorderColor = ImGui.GetColorU32(ChartColors.AxisLine);
        var buttonHovered = mousePos.X >= toggleButtonPos.X && mousePos.X <= toggleButtonPos.X + toggleButtonWidth &&
                           mousePos.Y >= toggleButtonPos.Y && mousePos.Y <= toggleButtonPos.Y + toggleButtonHeight;
        
        if (buttonHovered)
        {
            buttonBgColor = ImGui.GetColorU32(new Vector4(ChartColors.GridLine.X, ChartColors.GridLine.Y, ChartColors.GridLine.Z, 0.9f));
        }
        
        drawList.AddRectFilled(toggleButtonPos, 
            new Vector2(toggleButtonPos.X + toggleButtonWidth, toggleButtonPos.Y + toggleButtonHeight), 
            buttonBgColor, 3f);
        drawList.AddRect(toggleButtonPos, 
            new Vector2(toggleButtonPos.X + toggleButtonWidth, toggleButtonPos.Y + toggleButtonHeight), 
            buttonBorderColor, 3f);
        
        // Draw gear/settings icon
        var iconColor = ImGui.GetColorU32(_controlsDrawerOpen ? ChartColors.Neutral : ChartColors.TextPrimary);
        var iconCenter = new Vector2(toggleButtonPos.X + toggleButtonWidth / 2, toggleButtonPos.Y + toggleButtonHeight / 2);
        var iconRadius = 6f;
        
        drawList.AddCircle(iconCenter, iconRadius, iconColor, 8, 1.5f);
        drawList.AddCircleFilled(iconCenter, 2f, iconColor);
        
        // Handle toggle button click
        if (buttonHovered && ImGui.IsMouseClicked(0))
        {
            _controlsDrawerOpen = !_controlsDrawerOpen;
        }
        
        // Show tooltip
        if (buttonHovered)
        {
            ImGui.SetTooltip(_controlsDrawerOpen ? "Close controls" : "Open controls");
        }
        
        // Cache bounds for input blocking
        if (_controlsDrawerOpen)
        {
            _cachedControlsDrawerBounds = (
                new Vector2(drawerPos.X, Math.Min(drawerPos.Y, toggleButtonPos.Y)),
                new Vector2(toggleButtonPos.X + toggleButtonWidth, Math.Max(drawerPos.Y + drawerHeight, toggleButtonPos.Y + toggleButtonHeight)),
                true
            );
        }
        else
        {
            _cachedControlsDrawerBounds = (toggleButtonPos, 
                new Vector2(toggleButtonPos.X + toggleButtonWidth, toggleButtonPos.Y + toggleButtonHeight), 
                true);
        }
        
        if (!_controlsDrawerOpen) return;
        
        var drawerBgColor = ImGui.GetColorU32(new Vector4(ChartColors.FrameBackground.X, ChartColors.FrameBackground.Y, ChartColors.FrameBackground.Z, 0.92f));
        var drawerBorderColor = ImGui.GetColorU32(ChartColors.AxisLine);
        
        drawList.AddRectFilled(drawerPos, 
            new Vector2(drawerPos.X + drawerWidth, drawerPos.Y + drawerHeight), 
            drawerBgColor, 4f);
        drawList.AddRect(drawerPos, 
            new Vector2(drawerPos.X + drawerWidth, drawerPos.Y + drawerHeight), 
            drawerBorderColor, 4f);
        
        var contentX = drawerPos.X + drawerPadding;
        var contentY = drawerPos.Y + drawerPadding;
        
        // Draw Auto-Scroll checkbox
        var checkboxPos = new Vector2(contentX, contentY + (rowHeight - checkboxSize) / 2);
        var checkboxRowEnd = new Vector2(drawerPos.X + drawerWidth - drawerPadding, contentY + rowHeight);
        var checkboxRowHovered = mousePos.X >= contentX && mousePos.X <= checkboxRowEnd.X &&
                                mousePos.Y >= contentY && mousePos.Y <= checkboxRowEnd.Y;
        
        var checkboxBorderColor = checkboxRowHovered 
            ? ImGui.GetColorU32(ChartColors.TextPrimary) 
            : ImGui.GetColorU32(ChartColors.TextSecondary);
        drawList.AddRect(checkboxPos, 
            new Vector2(checkboxPos.X + checkboxSize, checkboxPos.Y + checkboxSize), 
            checkboxBorderColor, 2f);
        
        if (_config.AutoScrollEnabled)
        {
            var checkColor = ImGui.GetColorU32(ChartColors.Bullish);
            var checkPadding = 3f;
            drawList.AddLine(
                new Vector2(checkboxPos.X + checkPadding, checkboxPos.Y + checkboxSize / 2),
                new Vector2(checkboxPos.X + checkboxSize / 2, checkboxPos.Y + checkboxSize - checkPadding),
                checkColor, 2f);
            drawList.AddLine(
                new Vector2(checkboxPos.X + checkboxSize / 2, checkboxPos.Y + checkboxSize - checkPadding),
                new Vector2(checkboxPos.X + checkboxSize - checkPadding, checkboxPos.Y + checkPadding),
                checkColor, 2f);
        }
        
        var labelPos = new Vector2(checkboxPos.X + checkboxSize + 6, contentY + (rowHeight - ImGui.GetTextLineHeight()) / 2);
        var labelColor = ImGui.GetColorU32(_config.AutoScrollEnabled ? ChartColors.TextPrimary : ChartColors.TextSecondary);
        drawList.AddText(labelPos, labelColor, "Auto-scroll");
        
        if (checkboxRowHovered && ImGui.IsMouseClicked(0))
        {
            _config.AutoScrollEnabled = !_config.AutoScrollEnabled;
            OnAutoScrollSettingsChanged?.Invoke(_config.AutoScrollEnabled, _config.AutoScrollTimeValue, _config.AutoScrollTimeUnit, _config.AutoScrollNowPosition);
        }
        
        contentY += rowHeight;
        
        if (_config.AutoScrollEnabled)
        {
            const float valueBoxWidth = 50f;
            const float smallButtonWidth = 22f;
            const float spacing = 4f;
            
            // Draw "-" button
            var minusBtnPos = new Vector2(contentX, contentY + 2);
            var minusBtnHovered = mousePos.X >= minusBtnPos.X && mousePos.X <= minusBtnPos.X + smallButtonWidth &&
                                 mousePos.Y >= minusBtnPos.Y && mousePos.Y <= minusBtnPos.Y + rowHeight - 4;
            var minusBtnBg = minusBtnHovered 
                ? ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.35f, 0.8f))
                : ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.7f));
            drawList.AddRectFilled(minusBtnPos, new Vector2(minusBtnPos.X + smallButtonWidth, minusBtnPos.Y + rowHeight - 4), minusBtnBg, 3f);
            drawList.AddRect(minusBtnPos, new Vector2(minusBtnPos.X + smallButtonWidth, minusBtnPos.Y + rowHeight - 4), ImGui.GetColorU32(ChartColors.GridLine), 3f);
            var minusText = "-";
            var minusTextSize = ImGui.CalcTextSize(minusText);
            drawList.AddText(new Vector2(minusBtnPos.X + (smallButtonWidth - minusTextSize.X) / 2, minusBtnPos.Y + (rowHeight - 4 - minusTextSize.Y) / 2), 
                ImGui.GetColorU32(ChartColors.TextPrimary), minusText);
            
            if (minusBtnHovered && ImGui.IsMouseClicked(0) && _config.AutoScrollTimeValue > 1)
            {
                _config.AutoScrollTimeValue--;
                OnAutoScrollSettingsChanged?.Invoke(_config.AutoScrollEnabled, _config.AutoScrollTimeValue, _config.AutoScrollTimeUnit, _config.AutoScrollNowPosition);
            }
            
            // Value box
            var valueBoxPos = new Vector2(minusBtnPos.X + smallButtonWidth + spacing, contentY + 2);
            drawList.AddRectFilled(valueBoxPos, new Vector2(valueBoxPos.X + valueBoxWidth, valueBoxPos.Y + rowHeight - 4), 
                ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.8f)), 3f);
            drawList.AddRect(valueBoxPos, new Vector2(valueBoxPos.X + valueBoxWidth, valueBoxPos.Y + rowHeight - 4), 
                ImGui.GetColorU32(ChartColors.GridLine), 3f);
            var valueText = _config.AutoScrollTimeValue.ToString();
            var valueTextSize = ImGui.CalcTextSize(valueText);
            drawList.AddText(new Vector2(valueBoxPos.X + (valueBoxWidth - valueTextSize.X) / 2, valueBoxPos.Y + (rowHeight - 4 - valueTextSize.Y) / 2), 
                ImGui.GetColorU32(ChartColors.Neutral), valueText);
            
            // "+" button
            var plusBtnPos = new Vector2(valueBoxPos.X + valueBoxWidth + spacing, contentY + 2);
            var plusBtnHovered = mousePos.X >= plusBtnPos.X && mousePos.X <= plusBtnPos.X + smallButtonWidth &&
                                mousePos.Y >= plusBtnPos.Y && mousePos.Y <= plusBtnPos.Y + rowHeight - 4;
            var plusBtnBg = plusBtnHovered 
                ? ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.35f, 0.8f))
                : ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.7f));
            drawList.AddRectFilled(plusBtnPos, new Vector2(plusBtnPos.X + smallButtonWidth, plusBtnPos.Y + rowHeight - 4), plusBtnBg, 3f);
            drawList.AddRect(plusBtnPos, new Vector2(plusBtnPos.X + smallButtonWidth, plusBtnPos.Y + rowHeight - 4), ImGui.GetColorU32(ChartColors.GridLine), 3f);
            var plusText = "+";
            var plusTextSize = ImGui.CalcTextSize(plusText);
            drawList.AddText(new Vector2(plusBtnPos.X + (smallButtonWidth - plusTextSize.X) / 2, plusBtnPos.Y + (rowHeight - 4 - plusTextSize.Y) / 2), 
                ImGui.GetColorU32(ChartColors.TextPrimary), plusText);
            
            if (plusBtnHovered && ImGui.IsMouseClicked(0) && _config.AutoScrollTimeValue < 999)
            {
                _config.AutoScrollTimeValue++;
                OnAutoScrollSettingsChanged?.Invoke(_config.AutoScrollEnabled, _config.AutoScrollTimeValue, _config.AutoScrollTimeUnit, _config.AutoScrollNowPosition);
            }
            
            contentY += rowHeight;
            
            // Unit selector buttons
            var unitButtonWidth = (drawerWidth - drawerPadding * 2 - spacing * (TimeUnitNames.Length - 1)) / TimeUnitNames.Length;
            var unitButtonHeight = rowHeight - 4;
            
            for (var i = 0; i < TimeUnitNames.Length; i++)
            {
                var unitBtnPos = new Vector2(contentX + i * (unitButtonWidth + spacing), contentY + 2);
                var isSelected = (int)_config.AutoScrollTimeUnit == i;
                var unitBtnHovered = mousePos.X >= unitBtnPos.X && mousePos.X <= unitBtnPos.X + unitButtonWidth &&
                                    mousePos.Y >= unitBtnPos.Y && mousePos.Y <= unitBtnPos.Y + unitButtonHeight;
                
                var unitBtnBg = isSelected 
                    ? ImGui.GetColorU32(new Vector4(ChartColors.Neutral.X, ChartColors.Neutral.Y, ChartColors.Neutral.Z, 0.35f))
                    : unitBtnHovered 
                        ? ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.6f))
                        : ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 0.5f));
                var unitBtnBorder = isSelected 
                    ? ImGui.GetColorU32(ChartColors.Neutral) 
                    : ImGui.GetColorU32(ChartColors.GridLine);
                
                drawList.AddRectFilled(unitBtnPos, new Vector2(unitBtnPos.X + unitButtonWidth, unitBtnPos.Y + unitButtonHeight), unitBtnBg, 3f);
                drawList.AddRect(unitBtnPos, new Vector2(unitBtnPos.X + unitButtonWidth, unitBtnPos.Y + unitButtonHeight), unitBtnBorder, 3f);
                
                var unitText = TimeUnitNames[i];
                var unitTextSize = ImGui.CalcTextSize(unitText);
                var unitTextColor = isSelected ? ImGui.GetColorU32(ChartColors.Neutral) : ImGui.GetColorU32(ChartColors.TextPrimary);
                drawList.AddText(new Vector2(unitBtnPos.X + (unitButtonWidth - unitTextSize.X) / 2, unitBtnPos.Y + (unitButtonHeight - unitTextSize.Y) / 2), 
                    unitTextColor, unitText);
                
                if (unitBtnHovered && ImGui.IsMouseClicked(0))
                {
                    _config.AutoScrollTimeUnit = (TimeUnit)i;
                    OnAutoScrollSettingsChanged?.Invoke(_config.AutoScrollEnabled, _config.AutoScrollTimeValue, _config.AutoScrollTimeUnit, _config.AutoScrollNowPosition);
                }
            }
            
            contentY += rowHeight;
            
            // Position slider
            var sliderLabelPos = new Vector2(contentX, contentY + (rowHeight - ImGui.GetTextLineHeight()) / 2);
            drawList.AddText(sliderLabelPos, ImGui.GetColorU32(ChartColors.TextSecondary), "Position:");
            
            contentY += rowHeight;
            
            const float sliderHeight = 8f;
            var sliderTrackPos = new Vector2(contentX, contentY + (rowHeight - sliderHeight) / 2 - 4);
            var sliderTrackWidth = drawerWidth - drawerPadding * 2;
            
            drawList.AddRectFilled(sliderTrackPos, new Vector2(sliderTrackPos.X + sliderTrackWidth, sliderTrackPos.Y + sliderHeight), 
                ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 0.8f)), 4f);
            drawList.AddRect(sliderTrackPos, new Vector2(sliderTrackPos.X + sliderTrackWidth, sliderTrackPos.Y + sliderHeight), 
                ImGui.GetColorU32(ChartColors.GridLine), 4f);
            
            var fillWidth = sliderTrackWidth * (_config.AutoScrollNowPosition / 100f);
            if (fillWidth > 0)
            {
                drawList.AddRectFilled(sliderTrackPos, new Vector2(sliderTrackPos.X + fillWidth, sliderTrackPos.Y + sliderHeight), 
                    ImGui.GetColorU32(new Vector4(ChartColors.Neutral.X, ChartColors.Neutral.Y, ChartColors.Neutral.Z, 0.4f)), 4f);
            }
            
            const float handleWidth = 12f;
            const float handleHeight = 16f;
            var handleX = sliderTrackPos.X + fillWidth - handleWidth / 2;
            handleX = Math.Clamp(handleX, sliderTrackPos.X, sliderTrackPos.X + sliderTrackWidth - handleWidth);
            var handleY = sliderTrackPos.Y + sliderHeight / 2 - handleHeight / 2;
            
            var sliderHovered = mousePos.X >= sliderTrackPos.X - 5 && mousePos.X <= sliderTrackPos.X + sliderTrackWidth + 5 &&
                               mousePos.Y >= handleY && mousePos.Y <= handleY + handleHeight;
            
            var handleColor = sliderHovered 
                ? ImGui.GetColorU32(ChartColors.Neutral)
                : ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1f));
            drawList.AddRectFilled(new Vector2(handleX, handleY), new Vector2(handleX + handleWidth, handleY + handleHeight), handleColor, 3f);
            drawList.AddRect(new Vector2(handleX, handleY), new Vector2(handleX + handleWidth, handleY + handleHeight), 
                ImGui.GetColorU32(ChartColors.AxisLine), 3f);
            
            if (sliderHovered && ImGui.IsMouseDown(0))
            {
                var relativeX = mousePos.X - sliderTrackPos.X;
                var newPosition = Math.Clamp(relativeX / sliderTrackWidth * 100f, 0f, 100f);
                if (Math.Abs(newPosition - _config.AutoScrollNowPosition) > 0.5f)
                {
                    _config.AutoScrollNowPosition = newPosition;
                    OnAutoScrollSettingsChanged?.Invoke(_config.AutoScrollEnabled, _config.AutoScrollTimeValue, _config.AutoScrollTimeUnit, _config.AutoScrollNowPosition);
                }
            }
            
            var percentText = $"{_config.AutoScrollNowPosition:F0}%";
            var percentTextSize = ImGui.CalcTextSize(percentText);
            drawList.AddText(new Vector2(sliderTrackPos.X + sliderTrackWidth - percentTextSize.X, sliderTrackPos.Y + sliderHeight + 2), 
                ImGui.GetColorU32(ChartColors.TextSecondary), percentText);
        }
    }

    #endregion

    #region Utilities
    
    /// <summary>
    /// Toggles the visibility of a series by name.
    /// </summary>
    private void ToggleSeriesVisibility(string seriesName)
    {
        if (!_hiddenSeries.Add(seriesName))
        {
            _hiddenSeries.Remove(seriesName);
        }
    }
    
    /// <summary>
    /// Updates the cached sorted series list if the series data has changed.
    /// Uses a hash of series names and visibility to detect changes without allocating.
    /// </summary>
    private void UpdateSortedSeriesCache(PreparedGraphData data)
    {
        // Compute a simple hash of the series collection including visibility state
        var hash = data.Series.Count;
        foreach (var s in data.Series)
        {
            hash = HashCode.Combine(hash, s.Name, s.Visible);
        }
        
        // Only rebuild if changed
        if (hash != _lastSeriesHash)
        {
            _lastSeriesHash = hash;
            _sortedSeriesCache.Clear();
            _sortedSeriesCache.AddRange(data.Series);
            _sortedSeriesCache.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static Vector3[] GetSeriesColors(int count)
    {
        var colors = new Vector3[]
        {
            new(1.0f, 0.25f, 0.25f),   // Bright Red
            new(0.25f, 0.50f, 1.0f),   // Bright Blue
            new(0.20f, 0.90f, 0.40f),  // Bright Green
            new(0.75f, 0.30f, 0.90f),  // Bright Purple
            new(1.0f, 0.45f, 0.70f),   // Bright Pink
            new(1.0f, 0.85f, 0.0f),    // Bright Yellow
            new(1.0f, 0.55f, 0.0f),    // Bright Orange
            new(0.0f, 0.85f, 0.85f),   // Bright Cyan
        };

        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
            result[i] = colors[i % colors.Length];
        return result;
    }

    private string FormatValue(float v)
    {
        if (Math.Abs(v - Math.Truncate(v)) < _config.FloatEpsilon)
            return ((long)v).ToString("N0", CultureInfo.InvariantCulture);
        return v.ToString("N2", CultureInfo.InvariantCulture);
    }
    
    /// <summary>
    /// Binary search to find the index of the point whose X value bracket contains the target.
    /// Returns the index where xValues[idx] <= target < xValues[idx+1], or -1 if out of range.
    /// For step/stair-style graphs, this returns the value that should be displayed at the target X.
    /// </summary>
    private static int BinarySearchNearestX(double[] xValues, int count, double target)
    {
        if (count == 0) return -1;
        
        // Handle edge cases
        if (target < xValues[0]) return -1;
        if (target >= xValues[count - 1]) return count - 1;
        
        // Binary search for the interval containing target
        var left = 0;
        var right = count - 1;
        
        while (left < right)
        {
            var mid = left + (right - left + 1) / 2;
            if (xValues[mid] <= target)
            {
                left = mid;
            }
            else
            {
                right = mid - 1;
            }
        }
        
        return left;
    }

    #endregion
}
