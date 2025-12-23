using System.Globalization;
using Dalamud.Bindings.ImPlot;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;

namespace Kaleidoscope.Gui.Widgets;

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
/// </summary>
public class ImplotGraphWidget
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
        public AutoScrollTimeUnit AutoScrollTimeUnit { get; set; } = AutoScrollTimeUnit.Hours;
        
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
    
    /// <summary>
    /// Cached controls drawer bounds from the previous frame.
    /// </summary>
    private (Vector2 min, Vector2 max, bool valid) _cachedControlsDrawerBounds = (Vector2.Zero, Vector2.Zero, false);
    
    /// <summary>
    /// Names for auto-scroll time unit buttons.
    /// </summary>
    private static readonly string[] TimeUnitNames = { "sec", "min", "hr", "day", "wk" };
    
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
    /// Event fired when auto-scroll settings are changed via the controls drawer.
    /// Parameters: (bool autoScrollEnabled, int timeValue, AutoScrollTimeUnit timeUnit, float nowPosition)
    /// </summary>
    public event Action<bool, int, AutoScrollTimeUnit, float>? OnAutoScrollSettingsChanged;
    
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
        var formatted = FormatAbbreviated(value);
        var len = Math.Min(formatted.Length, size - 1);
        for (var i = 0; i < len; i++)
            buff[i] = (byte)formatted[i];
        buff[len] = 0;
        return len;
    };
    
    /// <summary>
    /// Formats a number with abbreviated notation (K, M, B).
    /// </summary>
    private static string FormatAbbreviated(double value)
    {
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000:0.##}B",
            >= 1_000_000 => $"{value / 1_000_000:0.##}M",
            >= 1_000 => $"{value / 1_000:0.##}K",
            _ => $"{value:0.##}"
        };
    }
    
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
        
        // Draw price label on the right
        if (!string.IsNullOrEmpty(label))
        {
            var labelSize = ImGui.CalcTextSize(label);
            var labelPos = new Vector2(p2.X - labelSize.X - 4, p2.Y - labelSize.Y / 2);
            
            // Background for label
            var bgMin = new Vector2(labelPos.X - 4, labelPos.Y - 2);
            var bgMax = new Vector2(p2.X, labelPos.Y + labelSize.Y + 2);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.85f)), 2f);
            
            // Label text
            drawList.AddText(labelPos, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)), label);
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
        
        // Draw value label on Y axis
        var valueLabel = FormatAbbreviated(valueAtMouse);
        var labelSize = ImGui.CalcTextSize(valueLabel);
        var labelPos = new Vector2(hRight.X - labelSize.X - 6, hRight.Y - labelSize.Y / 2);
        
        // Background box
        var bgPadding = 3f;
        drawList.AddRectFilled(
            new Vector2(labelPos.X - bgPadding, labelPos.Y - bgPadding),
            new Vector2(labelPos.X + labelSize.X + bgPadding, labelPos.Y + labelSize.Y + bgPadding),
            ImGui.GetColorU32(ChartColors.TooltipBackground), 2f);
        drawList.AddRect(
            new Vector2(labelPos.X - bgPadding, labelPos.Y - bgPadding),
            new Vector2(labelPos.X + labelSize.X + bgPadding, labelPos.Y + labelSize.Y + bgPadding),
            ImGui.GetColorU32(ChartColors.TooltipBorder), 2f);
        
        drawList.AddText(labelPos, ImGui.GetColorU32(ChartColors.TextPrimary), valueLabel);
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
        AutoScrollTimeUnit autoScrollTimeUnit = AutoScrollTimeUnit.Hours,
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
    public AutoScrollTimeUnit AutoScrollTimeUnit
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

    /// <summary>
    /// Draws the graph with the provided samples using ImPlot (trading platform style).
    /// </summary>
    /// <param name="samples">The sample data to plot.</param>
    public unsafe void Draw(IReadOnlyList<float> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ChartColors.TextSecondary);
            ImGui.TextUnformatted(_config.NoDataText);
            ImGui.PopStyleColor();
            return;
        }

        try
        {
            var avail = ImGui.GetContentRegionAvail();
            var plotSize = new Vector2(Math.Max(1f, avail.X), Math.Max(1f, avail.Y));

            // Calculate Y-axis bounds with auto-scaling
            float yMin, yMax;
            {
                var dataMin = float.MaxValue;
                var dataMax = float.MinValue;
                foreach (var val in samples)
                {
                    if (val < dataMin) dataMin = val;
                    if (val > dataMax) dataMax = val;
                }
                
                if (dataMin == float.MaxValue || dataMax == float.MinValue)
                {
                    dataMin = 0;
                    dataMax = 100;
                }

                var dataRange = dataMax - dataMin;
                if (dataRange < _config.FloatEpsilon)
                {
                    dataRange = Math.Max(dataMax * 0.1f, 1f);
                }
                yMin = Math.Max(0f, dataMin - dataRange * 0.15f);
                yMax = dataMax + dataRange * 0.15f;
            }

            if (Math.Abs(yMax - yMin) < _config.FloatEpsilon)
            {
                yMax = yMin + 1f;
            }

            // Calculate X-axis range with right padding (5% of data range)
            var xDataMax = (double)samples.Count;
            var xPadding = Math.Max(xDataMax * 0.05, 1.0); // At least 1 unit padding
            var xMax = xDataMax + xPadding;

            // Configure plot flags - trading platform style: no clutter
            var plotFlags = ImPlotFlags.NoTitle | ImPlotFlags.NoLegend | ImPlotFlags.NoMenus | 
                           ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText | ImPlotFlags.Crosshairs;
            
            // Apply trading platform styling
            PushChartStyle();
            
            // Set up axis limits before BeginPlot
            ImPlot.SetNextAxesLimits(0, xMax, yMin, yMax, ImPlotCond.Once);

            if (ImPlot.BeginPlot($"##{_config.PlotId}", plotSize, plotFlags))
            {
                // Configure axes - Y-axis on right side (trading style), with grid
                var xAxisFlags = ImPlotAxisFlags.NoTickLabels;
                var yAxisFlags = ImPlotAxisFlags.Opposite; // Right side Y-axis
                if (_config.ShowGridLines)
                {
                    yAxisFlags |= ImPlotAxisFlags.AutoFit;
                }
                else
                {
                    yAxisFlags |= ImPlotAxisFlags.NoGridLines;
                    xAxisFlags |= ImPlotAxisFlags.NoGridLines;
                }
                
                ImPlot.SetupAxes("", "", xAxisFlags, yAxisFlags);
                
                // Format Y-axis with abbreviated values
                ImPlot.SetupAxisFormat(ImAxis.Y1, YAxisFormatter);
                
                // Constrain axes to prevent negative values
                ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, 0, double.MaxValue);
                ImPlot.SetupAxisLimitsConstraints(ImAxis.Y1, 0, double.MaxValue);

                // Plot invisible dummy points at padded extents for auto-fit on double-click
                // This ensures auto-fit includes our desired padding
                var dummyX = stackalloc double[2] { 0, xMax };
                var dummyY = stackalloc double[2] { yMin > 0 ? yMin : 0, yMax };
                ImPlot.SetNextMarkerStyle(ImPlotMarker.None);
                ImPlot.SetNextLineStyle(new Vector4(0, 0, 0, 0), 0); // Invisible
                ImPlot.PlotLine("##padding", dummyX, dummyY, 2);

                // Convert samples to arrays for plotting
                var xValues = new double[samples.Count];
                var yValues = new double[samples.Count];
                for (var i = 0; i < samples.Count; i++)
                {
                    xValues[i] = i;
                    yValues[i] = samples[i];
                }
                
                // Determine if trend is bullish or bearish
                var isBullish = samples.Count < 2 || samples[^1] >= samples[0];
                var lineColor = isBullish ? ChartColors.Bullish : ChartColors.Bearish;
                var fillTopColor = isBullish ? ChartColors.BullishFillTop : ChartColors.BearishFillTop;
                var fillBottomColor = isBullish ? ChartColors.BullishFillBottom : ChartColors.BearishFillBottom;
                
                ImPlot.SetNextLineStyle(lineColor, 2f);

                // Plot based on configured graph type
                fixed (double* xPtr = xValues)
                fixed (double* yPtr = yValues)
                {
                    switch (_config.GraphType)
                    {
                        case GraphType.Line:
                            ImPlot.PlotLine("Gil", xPtr, yPtr, samples.Count);
                            break;
                        case GraphType.Stairs:
                            ImPlot.PlotStairs("Gil", xPtr, yPtr, samples.Count);
                            break;
                        case GraphType.Bars:
                            ImPlot.SetNextFillStyle(lineColor);
                            ImPlot.PlotBars("Gil", xPtr, yPtr, samples.Count, 0.67);
                            break;
                        case GraphType.Area:
                        default:
                            // Draw shaded area from data line down to Y=0 using ImPlot's built-in function
                            ImPlot.SetNextFillStyle(fillTopColor);
                            ImPlot.PlotShaded("Gil##shaded", xPtr, yPtr, samples.Count, 0.0);
                            // Set line style again (PlotShaded consumed the previous one)
                            ImPlot.SetNextLineStyle(lineColor, 2f);
                            ImPlot.PlotLine("Gil", xPtr, yPtr, samples.Count);
                            break;
                    }
                }
                
                // Draw current price horizontal line
                if (_config.ShowCurrentPriceLine && samples.Count > 0)
                {
                    var currentValue = samples[^1];
                    var priceLineColor = isBullish ? ChartColors.Bullish : ChartColors.Bearish;
                    DrawPriceLine(currentValue, FormatAbbreviated(currentValue), priceLineColor, 1.5f, true);
                }

                // Show crosshair and tooltip on hover
                if (ImPlot.IsPlotHovered())
                {
                    var mousePos = ImPlot.GetPlotMousePos();
                    var mouseX = mousePos.X;
                    
                    // Find the nearest sample index
                    var nearestIdx = (int)Math.Round(mouseX);
                    if (nearestIdx >= 0 && nearestIdx < samples.Count)
                    {
                        var value = samples[nearestIdx];
                        
                        // Draw crosshair
                        if (_config.ShowCrosshair)
                        {
                            DrawCrosshair(nearestIdx, value, value);
                        }
                        
                        // Draw tooltip box
                        var screenPos = ImPlot.PlotToPixels(nearestIdx, value);
                        var changePercent = nearestIdx > 0 ? (value - samples[nearestIdx - 1]) / samples[nearestIdx - 1] * 100 : 0;
                        var changeSign = changePercent >= 0 ? "+" : "";
                        var tooltipLines = new[]
                        {
                            $"Value: {FormatValue(value)}",
                            $"Change: {changeSign}{changePercent:F2}%"
                        };
                        DrawTooltipBox(screenPos, tooltipLines, isBullish ? ChartColors.Bullish : ChartColors.Bearish);
                    }
                }

                // Draw value label annotation if enabled
                if (_config.ShowValueLabel && samples.Count > 0)
                {
                    var lastValue = samples[^1];
                    var text = FormatValue(lastValue);
                    var pixOffset = new Vector2(_config.ValueLabelOffsetX, _config.ValueLabelOffsetY);
                    var labelColor = isBullish ? ChartColors.Bullish : ChartColors.Bearish;
                    ImPlot.Annotation(samples.Count - 1, lastValue, labelColor, pixOffset, true, text);
                }

                ImPlot.EndPlot();
            }
            
            PopChartStyle();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ImplotGraphWidget] Graph rendering error: {ex.Message}");
            ImGui.TextUnformatted("Error rendering graph");
        }
    }

    /// <summary>
    /// Draws multiple data series overlaid on the same graph with time-aligned data.
    /// All lines extend to current time using their last recorded value.
    /// Trading platform style with crosshairs and styled tooltips.
    /// </summary>
    /// <param name="series">List of data series with names and timestamped values.</param>
    public unsafe void DrawMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series)
    {
        if (series == null || series.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ChartColors.TextSecondary);
            ImGui.TextUnformatted(_config.NoDataText);
            ImGui.PopStyleColor();
            return;
        }

        // Find global time range across all series
        var globalMinTime = DateTime.MaxValue;
        var globalMaxTime = DateTime.Now;

        foreach (var (_, samples) in series)
        {
            if (samples == null || samples.Count == 0) continue;
            if (samples[0].ts < globalMinTime) globalMinTime = samples[0].ts;
        }

        if (globalMinTime == DateTime.MaxValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ChartColors.TextSecondary);
            ImGui.TextUnformatted(_config.NoDataText);
            ImGui.PopStyleColor();
            return;
        }

        var totalTimeSpan = (globalMaxTime - globalMinTime).TotalSeconds;
        if (totalTimeSpan < 1) totalTimeSpan = 1;

        // Add right padding (5% of time range)
        var xPadding = Math.Max(totalTimeSpan * 0.05, 1.0); // At least 1 second padding
        var xMax = totalTimeSpan + xPadding;
        var colors = GetSeriesColors(series.Count);

        try
        {
            var avail = ImGui.GetContentRegionAvail();
            
            // Reserve space for scrollable legend on the right (if enabled and position is Outside)
            var useOutsideLegend = _config.ShowLegend && _config.LegendPosition == LegendPosition.Outside;
            var legendWidth = useOutsideLegend ? _config.LegendWidth : 0f;
            var legendPadding = useOutsideLegend ? 5f : 0f;
            var plotWidth = Math.Max(1f, avail.X - legendWidth - legendPadding);
            var plotSize = new Vector2(plotWidth, Math.Max(1f, avail.Y));

            // Calculate X-axis limits first (needed to filter visible data for Y-axis auto-scaling)
            double xMinLimit, xMaxLimit;
            if (_config.AutoScrollEnabled)
            {
                // Auto-scroll mode: X-axis is exactly timeRange wide
                // NowPosition controls where "now" appears (0% = left, 50% = center, 100% = right)
                var timeRangeSeconds = _config.GetAutoScrollTimeRangeSeconds();
                var nowFraction = _config.AutoScrollNowPosition / 100f;
                var leftPortion = timeRangeSeconds * nowFraction;
                var rightPortion = timeRangeSeconds * (1f - nowFraction);
                xMinLimit = totalTimeSpan - leftPortion;
                xMaxLimit = totalTimeSpan + rightPortion;
            }
            else
            {
                // Normal mode: show full range, allow user zoom/pan
                xMinLimit = 0;
                xMaxLimit = xMax;
            }

            // Calculate Y-axis bounds based on visible data (excluding hidden series)
            // When auto-scrolling, only consider data points within the visible time range
            float yMin, yMax;
            {
                var dataMin = float.MaxValue;
                var dataMax = float.MinValue;
                foreach (var (name, samples) in series)
                {
                    if (samples == null) continue;
                    // Skip hidden series in auto-scaling
                    if (_hiddenSeries.Contains(name)) continue;
                    
                    // Track last value before visible range for proper Y bounds
                    float? lastValueBeforeRange = null;
                    
                    foreach (var (ts, val) in samples)
                    {
                        var xPos = (ts - globalMinTime).TotalSeconds;
                        
                        // When auto-scrolling, only consider data within visible X range
                        if (_config.AutoScrollEnabled)
                        {
                            if (xPos < xMinLimit)
                            {
                                // Track last value before visible range (used for line extension)
                                lastValueBeforeRange = val;
                                continue;
                            }
                            if (xPos > xMaxLimit) continue;
                        }
                        
                        if (val < dataMin) dataMin = val;
                        if (val > dataMax) dataMax = val;
                    }
                    
                    // Include the last value before visible range (line extends from there)
                    if (_config.AutoScrollEnabled && lastValueBeforeRange.HasValue)
                    {
                        if (lastValueBeforeRange.Value < dataMin) dataMin = lastValueBeforeRange.Value;
                        if (lastValueBeforeRange.Value > dataMax) dataMax = lastValueBeforeRange.Value;
                    }
                }

                if (dataMin == float.MaxValue || dataMax == float.MinValue)
                {
                    dataMin = 0;
                    dataMax = 100;
                }

                var dataRange = dataMax - dataMin;
                if (dataRange < _config.FloatEpsilon)
                {
                    dataRange = Math.Max(dataMax * 0.1f, 1f);
                }
                yMin = Math.Max(0f, dataMin - dataRange * 0.15f);
                yMax = dataMax + dataRange * 0.15f;
            }

            // Configure plot flags - trading platform style with crosshairs
            var plotFlags = ImPlotFlags.NoTitle | ImPlotFlags.NoMenus | ImPlotFlags.NoBoxSelect | 
                           ImPlotFlags.NoMouseText | ImPlotFlags.NoLegend | ImPlotFlags.Crosshairs;
            
            // Disable plot input when mouse is over any overlay (legend or controls drawer)
            if (IsMouseOverOverlay)
            {
                plotFlags |= ImPlotFlags.NoInputs;
            }
            
            // Apply trading platform styling
            PushChartStyle();
            
            // Set axis limits based on mode
            if (_config.AutoScrollEnabled)
            {
                // Use Always condition to force the view to follow
                ImPlot.SetNextAxesLimits(xMinLimit, xMaxLimit, yMin, yMax, ImPlotCond.Always);
            }
            else
            {
                ImPlot.SetNextAxesLimits(xMinLimit, xMaxLimit, yMin, yMax, ImPlotCond.Once);
            }

            if (ImPlot.BeginPlot($"##{_config.PlotId}_multi", plotSize, plotFlags))
            {
                // Configure axes - Y-axis on right (trading style), with optional grid
                var xAxisFlags = _config.ShowXAxisTimestamps 
                    ? ImPlotAxisFlags.None 
                    : ImPlotAxisFlags.NoTickLabels;
                var yAxisFlags = ImPlotAxisFlags.Opposite; // Right side Y-axis
                
                if (!_config.ShowGridLines)
                {
                    xAxisFlags |= ImPlotAxisFlags.NoGridLines;
                    yAxisFlags |= ImPlotAxisFlags.NoGridLines;
                }
                
                ImPlot.SetupAxes("", "", xAxisFlags, yAxisFlags);
                
                // Format X-axis with time labels if enabled
                if (_config.ShowXAxisTimestamps)
                {
                    ImPlot.SetupAxisFormat(ImAxis.X1, XAxisTimeFormatter, (void*)(long)globalMinTime.Ticks);
                }
                
                // Format Y-axis with abbreviated values
                ImPlot.SetupAxisFormat(ImAxis.Y1, YAxisFormatter);

                // Constrain axes to prevent negative values
                ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, 0, double.MaxValue);
                ImPlot.SetupAxisLimitsConstraints(ImAxis.Y1, 0, double.MaxValue);

                // Plot invisible dummy points at padded extents for auto-fit on double-click
                // This ensures auto-fit includes our desired padding
                var dummyX = stackalloc double[2] { 0, xMax };
                var dummyY = stackalloc double[2] { yMin > 0 ? yMin : 0, yMax };
                ImPlot.SetNextMarkerStyle(ImPlotMarker.None);
                ImPlot.SetNextLineStyle(new Vector4(0, 0, 0, 0), 0); // Invisible
                ImPlot.PlotLine("##padding", dummyX, dummyY, 2);

                // Draw each series
                for (var seriesIdx = 0; seriesIdx < series.Count; seriesIdx++)
                {
                    var (name, samples) = series[seriesIdx];
                    if (samples == null || samples.Count == 0) continue;
                    
                    // Skip hidden series - don't draw them on the chart
                    if (_hiddenSeries.Contains(name)) continue;

                    // Build arrays for this series, including extension to current time
                    var pointCount = samples.Count + 1; // +1 for extension to current time
                    var xValues = new double[pointCount];
                    var yValues = new double[pointCount];

                    for (var i = 0; i < samples.Count; i++)
                    {
                        xValues[i] = (samples[i].ts - globalMinTime).TotalSeconds;
                        yValues[i] = samples[i].value;
                    }

                    // Extend to current time with last value
                    xValues[samples.Count] = totalTimeSpan;
                    yValues[samples.Count] = samples[^1].value;

                    // Set color for this series
                    var color = colors[seriesIdx];
                    var colorVec4 = new Vector4(color.X, color.Y, color.Z, 1f);
                    ImPlot.SetNextLineStyle(colorVec4, 2f);

                    // Plot based on configured graph type
                    fixed (double* xPtr = xValues)
                    fixed (double* yPtr = yValues)
                    {
                        switch (_config.GraphType)
                        {
                            case GraphType.Line:
                                ImPlot.PlotLine(name, xPtr, yPtr, pointCount);
                                break;
                            case GraphType.Stairs:
                                ImPlot.PlotStairs(name, xPtr, yPtr, pointCount);
                                break;
                            case GraphType.Bars:
                                // For multi-series bars, use a smaller width and offset
                                var barWidth = totalTimeSpan / pointCount * 0.8 / series.Count;
                                ImPlot.SetNextFillStyle(colorVec4);
                                ImPlot.PlotBars(name, xPtr, yPtr, pointCount, barWidth);
                                break;
                            case GraphType.Area:
                            default:
                                // Draw shaded area from data line down to Y=0 using ImPlot's built-in function
                                ImPlot.SetNextFillStyle(new Vector4(color.X, color.Y, color.Z, 0.55f));
                                ImPlot.PlotShaded($"{name}##shaded", xPtr, yPtr, pointCount, 0.0);
                                // Set line style again (PlotShaded consumed the previous one)
                                ImPlot.SetNextLineStyle(colorVec4, 2f);
                                ImPlot.PlotLine(name, xPtr, yPtr, pointCount);
                                break;
                        }
                    }

                }

                // Show hover tooltip with series name and value at mouse position
                if (ImPlot.IsPlotHovered())
                {
                    var mousePos = ImPlot.GetPlotMousePos();
                    var mouseX = mousePos.X; // Time in seconds from globalMinTime
                    var mouseY = mousePos.Y;
                    
                    // Find the nearest series and value at this time position
                    var nearestSeriesName = string.Empty;
                    var nearestValue = 0f;
                    var nearestColor = new Vector3(1f, 1f, 1f);
                    var minYDistance = double.MaxValue;
                    var foundPoint = false;
                    var pointX = 0.0;
                    var pointY = 0.0;
                    
                    for (var seriesIdx = 0; seriesIdx < series.Count; seriesIdx++)
                    {
                        var (name, samples) = series[seriesIdx];
                        if (samples == null || samples.Count == 0) continue;
                        
                        // Skip hidden series in hover detection
                        if (_hiddenSeries.Contains(name)) continue;
                        
                        // Find value at mouseX time using interpolation
                        float valueAtTime;
                        double actualX;
                        var foundInSeries = false;
                        
                        // Convert mouseX (time offset) to find appropriate sample
                        for (var i = 0; i < samples.Count; i++)
                        {
                            var sampleTimeOffset = (samples[i].ts - globalMinTime).TotalSeconds;
                            var nextTimeOffset = i < samples.Count - 1 
                                ? (samples[i + 1].ts - globalMinTime).TotalSeconds 
                                : totalTimeSpan;
                            
                            if (mouseX >= sampleTimeOffset && mouseX <= nextTimeOffset)
                            {
                                // Use the value at this point (step interpolation)
                                valueAtTime = samples[i].value;
                                actualX = sampleTimeOffset;
                                foundInSeries = true;
                                
                                var yDistance = Math.Abs(mouseY - valueAtTime);
                                if (yDistance < minYDistance)
                                {
                                    minYDistance = yDistance;
                                    nearestSeriesName = name;
                                    nearestValue = valueAtTime;
                                    nearestColor = colors[seriesIdx];
                                    pointX = mouseX;
                                    pointY = valueAtTime;
                                    foundPoint = true;
                                }
                                break;
                            }
                        }
                        
                        // Check if we're past the last sample (in the extended region)
                        if (!foundInSeries && samples.Count > 0)
                        {
                            var lastSampleTime = (samples[^1].ts - globalMinTime).TotalSeconds;
                            if (mouseX > lastSampleTime)
                            {
                                valueAtTime = samples[^1].value;
                                var yDistance = Math.Abs(mouseY - valueAtTime);
                                if (yDistance < minYDistance)
                                {
                                    minYDistance = yDistance;
                                    nearestSeriesName = name;
                                    nearestValue = valueAtTime;
                                    nearestColor = colors[seriesIdx];
                                    pointX = mouseX;
                                    pointY = valueAtTime;
                                    foundPoint = true;
                                }
                            }
                        }
                    }
                    
                    if (foundPoint)
                    {
                        // Draw crosshair at the point
                        if (_config.ShowCrosshair)
                        {
                            DrawCrosshair(pointX, pointY, nearestValue);
                        }
                        
                        // Draw styled tooltip box
                        var screenPos = ImPlot.PlotToPixels(pointX, pointY);
                        var accentColor = new Vector4(nearestColor.X, nearestColor.Y, nearestColor.Z, 1f);
                        var tooltipLines = new[]
                        {
                            nearestSeriesName,
                            $"Value: {FormatValue(nearestValue)}"
                        };
                        DrawTooltipBox(screenPos, tooltipLines, accentColor);
                    }
                }

                // Draw value labels after all lines are drawn, with vertical spacing to prevent overlap
                if (_config.ShowValueLabel)
                {
                    const float labelHeight = 18f; // Approximate height of each label
                    
                    // Collect all labels with their values for sorting
                    var labels = new List<(int idx, string name, float value, Vector3 color)>();
                    for (var seriesIdx = 0; seriesIdx < series.Count; seriesIdx++)
                    {
                        var (name, samples) = series[seriesIdx];
                        if (samples == null || samples.Count == 0) continue;
                        // Skip hidden series in value labels
                        if (_hiddenSeries.Contains(name)) continue;
                        labels.Add((seriesIdx, name, samples[^1].value, colors[seriesIdx]));
                    }
                    
                    // Sort by value descending so higher values get higher positions
                    labels.Sort((a, b) => b.value.CompareTo(a.value));
                    
                    // Draw labels with vertical offset based on sorted position
                    for (var i = 0; i < labels.Count; i++)
                    {
                        var (_, labelName, lastValue, color) = labels[i];
                        var text = $"{labelName}: {FormatValue(lastValue)}";
                        // Stack labels vertically: first (highest value) at top, subsequent ones below
                        var yOffset = _config.ValueLabelOffsetY + (i * labelHeight);
                        var pixOffset = new Vector2(_config.ValueLabelOffsetX, yOffset);
                        var labelColor = new Vector4(color.X, color.Y, color.Z, 0.9f);
                        ImPlot.Annotation(totalTimeSpan, lastValue, labelColor, pixOffset, true, text);
                    }
                }

                // Draw in-graph legend if position is inside
                if (_config.ShowLegend && _config.LegendPosition != LegendPosition.Outside)
                {
                    DrawInsideLegend(series, colors);
                }
                else
                {
                    // Invalidate cached bounds when not showing inside legend
                    _cachedLegendBounds = (Vector2.Zero, Vector2.Zero, false);
                }
                
                // Draw controls drawer (toggle button + panel) if enabled
                if (_config.ShowControlsDrawer)
                {
                    DrawControlsDrawer();
                }
                else
                {
                    // Invalidate cached bounds when not showing controls drawer
                    _cachedControlsDrawerBounds = (Vector2.Zero, Vector2.Zero, false);
                }

                ImPlot.EndPlot();
            }
            
            PopChartStyle();
            
            // Draw scrollable legend on the right (if enabled and position is Outside)
            if (_config.ShowLegend && _config.LegendPosition == LegendPosition.Outside)
            {
                ImGui.SameLine();
                DrawScrollableLegend(series, colors, _config.LegendWidth, avail.Y);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ImplotGraphWidget] Multi-series rendering error: {ex.Message}");
            ImGui.TextUnformatted("Error rendering graph");
        }
    }
    
    /// <summary>
    /// Draws a scrollable legend for multi-series graphs with trading platform styling.
    /// </summary>
    private void DrawScrollableLegend(
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series,
        Vector3[] colors,
        float width,
        float height)
    {
        // Style the legend panel with trading platform colors
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ChartColors.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, ChartColors.AxisLine);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, ChartColors.PlotBackground);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, ChartColors.GridLine);
        
        if (ImGui.BeginChild($"##{_config.PlotId}_legend", new Vector2(width, height), true))
        {
            // Create sorted list of indices by series name (character name)
            var sortedIndices = Enumerable.Range(0, series.Count)
                .OrderBy(i => series[i].name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            for (var idx = 0; idx < sortedIndices.Count; idx++)
            {
                var i = sortedIndices[idx];
                var (name, samples) = series[i];
                if (samples == null || samples.Count == 0) continue;
                
                var isHidden = _hiddenSeries.Contains(name);
                var color = colors[i];
                var lastValue = samples[^1].value;
                
                // Use dimmed color for hidden series
                var displayAlpha = isHidden ? 0.35f : 1f;
                
                // Draw colored square indicator (rounded for modern look)
                var drawList = ImGui.GetWindowDrawList();
                var cursorPos = ImGui.GetCursorScreenPos();
                const float indicatorSize = 10f;
                var colorU32 = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, displayAlpha));
                
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
                ImGui.TextUnformatted($"{name}");
                ImGui.PopStyleColor();
                
                // Make row clickable - use invisible button over the row area
                var rowEnd = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(rowStart);
                if (ImGui.InvisibleButton($"##legend_toggle_{name}", new Vector2(width - 16f, indicatorSize + 2f)))
                {
                    ToggleSeriesVisibility(name);
                }
                
                // Show tooltip on hover with styled content
                if (ImGui.IsItemHovered())
                {
                    var statusText = isHidden ? " (hidden)" : "";
                    ImGui.SetTooltip($"{name}: {FormatValue(lastValue)}{statusText}\nClick to toggle visibility");
                }
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(4);
    }
    
    /// <summary>
    /// Draws an interactive legend inside the plot area using ImPlot's draw list.
    /// The legend is positioned based on the LegendPosition config setting.
    /// Supports scrolling when there are too many series to fit.
    /// </summary>
    private void DrawInsideLegend(
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series,
        Vector3[] colors)
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
        foreach (var (name, samples) in series)
        {
            if (samples == null || samples.Count == 0) continue;
            var textSize = ImGui.CalcTextSize(name);
            maxTextWidth = Math.Max(maxTextWidth, textSize.X);
            validSeriesCount++;
        }
        
        if (validSeriesCount == 0) return;
        
        // Calculate content height and max display height based on percentage setting
        var contentHeight = validSeriesCount * rowHeight;
        var maxLegendHeight = plotSize.Y * (_config.LegendHeightPercent / 100f);
        maxLegendHeight = Math.Max(maxLegendHeight, rowHeight + padding * 2); // At least one row
        var needsScrolling = contentHeight > maxLegendHeight - padding * 2;
        
        var legendWidth = padding * 2 + indicatorSize + indicatorTextGap + maxTextWidth + (needsScrolling ? scrollbarWidth + 4f : 0f);
        var legendHeight = Math.Min(padding * 2 + contentHeight, maxLegendHeight);
        
        // Determine legend position based on config
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
        
        // Cache legend bounds for next frame's input blocking
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
        
        // Sort series by name
        var sortedIndices = Enumerable.Range(0, series.Count)
            .Where(i => series[i].samples != null && series[i].samples.Count > 0)
            .OrderBy(i => series[i].name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        // Calculate visible area for clipping
        var contentAreaTop = legendPos.Y + padding;
        var contentAreaBottom = legendPos.Y + legendHeight - padding;
        var contentAreaRight = legendPos.X + legendWidth - padding - (needsScrolling ? scrollbarWidth + 4f : 0f);
        
        // Draw each legend entry (with clipping)
        var yOffset = contentAreaTop - _insideLegendScrollOffset;
        var visibleRowIdx = 0;
        foreach (var i in sortedIndices)
        {
            var rowTop = yOffset;
            var rowBottom = yOffset + rowHeight;
            
            // Skip rows that are outside the visible area
            if (rowBottom < contentAreaTop || rowTop > contentAreaBottom)
            {
                yOffset += rowHeight;
                visibleRowIdx++;
                continue;
            }
            
            var (name, samples) = series[i];
            var isHidden = _hiddenSeries.Contains(name);
            var color = colors[i];
            var displayAlpha = isHidden ? 0.35f : 1f;
            
            // Check if mouse is over this row (only if row is visible)
            var mouseInRow = mouseInLegend && 
                            mousePos.Y >= Math.Max(rowTop, contentAreaTop) && 
                            mousePos.Y < Math.Min(rowBottom, contentAreaBottom) &&
                            rowTop >= contentAreaTop && rowBottom <= contentAreaBottom;
            
            // Handle click to toggle visibility
            if (mouseInRow && ImGui.IsMouseClicked(0))
            {
                ToggleSeriesVisibility(name);
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
            
            // Only draw content if the row is at least partially visible
            if (rowTop >= contentAreaTop - rowHeight && rowBottom <= contentAreaBottom + rowHeight)
            {
                // Draw color indicator
                var indicatorY = yOffset + (rowHeight - indicatorSize) / 2;
                if (indicatorY >= contentAreaTop - indicatorSize && indicatorY + indicatorSize <= contentAreaBottom + indicatorSize)
                {
                    var indicatorPos = new Vector2(legendPos.X + padding, indicatorY);
                    var colorU32 = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, displayAlpha));
                    
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
                        drawList.AddText(textPos, ImGui.GetColorU32(textColor), name);
                    }
                }
            }
            
            yOffset += rowHeight;
            visibleRowIdx++;
        }
        
        // Draw scrollbar if needed
        if (needsScrolling)
        {
            var scrollTrackTop = legendPos.Y + padding;
            var scrollTrackBottom = legendPos.Y + legendHeight - padding;
            var scrollTrackHeight = scrollTrackBottom - scrollTrackTop;
            var scrollTrackX = legendPos.X + legendWidth - padding - scrollbarWidth;
            
            // Draw track
            var trackColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
            drawList.AddRectFilled(
                new Vector2(scrollTrackX, scrollTrackTop),
                new Vector2(scrollTrackX + scrollbarWidth, scrollTrackBottom),
                trackColor, 3f);
            
            // Calculate thumb size and position
            var visibleRatio = (legendHeight - padding * 2) / contentHeight;
            var thumbHeight = Math.Max(20f, scrollTrackHeight * visibleRatio);
            var scrollRatio = maxScrollOffset > 0 ? _insideLegendScrollOffset / maxScrollOffset : 0f;
            var thumbTop = scrollTrackTop + scrollRatio * (scrollTrackHeight - thumbHeight);
            
            // Draw thumb
            var thumbColor = ImGui.GetColorU32(ChartColors.GridLine);
            drawList.AddRectFilled(
                new Vector2(scrollTrackX, thumbTop),
                new Vector2(scrollTrackX + scrollbarWidth, thumbTop + thumbHeight),
                thumbColor, 3f);
        }
        
        // Show tooltip when hovering over legend
        if (mouseInLegend)
        {
            // Find which row we're hovering based on scroll position
            var relativeY = mousePos.Y - contentAreaTop + _insideLegendScrollOffset;
            var hoveredIdx = (int)(relativeY / rowHeight);
            if (hoveredIdx >= 0 && hoveredIdx < sortedIndices.Count)
            {
                // Check if the row is actually visible
                var rowTop = contentAreaTop - _insideLegendScrollOffset + hoveredIdx * rowHeight;
                var rowBottom = rowTop + rowHeight;
                if (rowTop < contentAreaBottom && rowBottom > contentAreaTop)
                {
                    var (name, samples) = series[sortedIndices[hoveredIdx]];
                    var isHidden = _hiddenSeries.Contains(name);
                    var lastValue = samples[^1].value;
                    var statusText = isHidden ? " (hidden)" : "";
                    var scrollHint = needsScrolling ? "\nScroll to see more" : "";
                    ImGui.SetTooltip($"{name}: {FormatValue(lastValue)}{statusText}\nClick to toggle visibility{scrollHint}");
                }
            }
        }
    }
    
    /// <summary>
    /// Draws the controls drawer with toggle button and auto-scroll controls.
    /// The drawer slides out from the bottom-right corner of the plot.
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
        
        // Draw gear/settings icon (simple representation)
        var iconColor = ImGui.GetColorU32(_controlsDrawerOpen ? ChartColors.Neutral : ChartColors.TextPrimary);
        var iconCenter = new Vector2(toggleButtonPos.X + toggleButtonWidth / 2, toggleButtonPos.Y + toggleButtonHeight / 2);
        var iconRadius = 6f;
        
        // Draw a simple gear-like symbol (circle with lines)
        drawList.AddCircle(iconCenter, iconRadius, iconColor, 8, 1.5f);
        drawList.AddCircleFilled(iconCenter, 2f, iconColor);
        
        // Handle toggle button click
        if (buttonHovered && ImGui.IsMouseClicked(0))
        {
            _controlsDrawerOpen = !_controlsDrawerOpen;
        }
        
        // Show tooltip for toggle button
        if (buttonHovered)
        {
            ImGui.SetTooltip(_controlsDrawerOpen ? "Close controls" : "Open controls");
        }
        
        // Cache bounds for input blocking - include both button and drawer if open
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
        
        // Draw drawer panel if open
        if (!_controlsDrawerOpen) return;
        
        var drawerBgColor = ImGui.GetColorU32(new Vector4(ChartColors.FrameBackground.X, ChartColors.FrameBackground.Y, ChartColors.FrameBackground.Z, 0.92f));
        var drawerBorderColor = ImGui.GetColorU32(ChartColors.AxisLine);
        
        // Draw drawer background
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
        var checkboxHovered = mousePos.X >= checkboxPos.X && mousePos.X <= checkboxPos.X + checkboxSize &&
                             mousePos.Y >= checkboxPos.Y && mousePos.Y <= checkboxPos.Y + checkboxSize;
        
        // Make the entire row clickable for the checkbox
        var checkboxRowEnd = new Vector2(drawerPos.X + drawerWidth - drawerPadding, contentY + rowHeight);
        var checkboxRowHovered = mousePos.X >= contentX && mousePos.X <= checkboxRowEnd.X &&
                                mousePos.Y >= contentY && mousePos.Y <= checkboxRowEnd.Y;
        
        // Draw checkbox box
        var checkboxBorderColor = checkboxRowHovered 
            ? ImGui.GetColorU32(ChartColors.TextPrimary) 
            : ImGui.GetColorU32(ChartColors.TextSecondary);
        drawList.AddRect(checkboxPos, 
            new Vector2(checkboxPos.X + checkboxSize, checkboxPos.Y + checkboxSize), 
            checkboxBorderColor, 2f);
        
        // Draw checkmark if enabled
        if (_config.AutoScrollEnabled)
        {
            var checkColor = ImGui.GetColorU32(ChartColors.Bullish);
            var checkPadding = 3f;
            // Draw a checkmark
            drawList.AddLine(
                new Vector2(checkboxPos.X + checkPadding, checkboxPos.Y + checkboxSize / 2),
                new Vector2(checkboxPos.X + checkboxSize / 2, checkboxPos.Y + checkboxSize - checkPadding),
                checkColor, 2f);
            drawList.AddLine(
                new Vector2(checkboxPos.X + checkboxSize / 2, checkboxPos.Y + checkboxSize - checkPadding),
                new Vector2(checkboxPos.X + checkboxSize - checkPadding, checkboxPos.Y + checkPadding),
                checkColor, 2f);
        }
        
        // Draw checkbox label
        var labelPos = new Vector2(checkboxPos.X + checkboxSize + 6, contentY + (rowHeight - ImGui.GetTextLineHeight()) / 2);
        var labelColor = ImGui.GetColorU32(_config.AutoScrollEnabled ? ChartColors.TextPrimary : ChartColors.TextSecondary);
        drawList.AddText(labelPos, labelColor, "Auto-scroll");
        
        // Handle checkbox click
        if (checkboxRowHovered && ImGui.IsMouseClicked(0))
        {
            _config.AutoScrollEnabled = !_config.AutoScrollEnabled;
            OnAutoScrollSettingsChanged?.Invoke(_config.AutoScrollEnabled, _config.AutoScrollTimeValue, _config.AutoScrollTimeUnit, _config.AutoScrollNowPosition);
        }
        
        contentY += rowHeight;
        
        // Draw time range picker if auto-scroll is enabled
        if (_config.AutoScrollEnabled)
        {
            // Row 1: Value input with +/- buttons
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
            
            // Draw value display box
            var valueBoxPos = new Vector2(minusBtnPos.X + smallButtonWidth + spacing, contentY + 2);
            drawList.AddRectFilled(valueBoxPos, new Vector2(valueBoxPos.X + valueBoxWidth, valueBoxPos.Y + rowHeight - 4), 
                ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.8f)), 3f);
            drawList.AddRect(valueBoxPos, new Vector2(valueBoxPos.X + valueBoxWidth, valueBoxPos.Y + rowHeight - 4), 
                ImGui.GetColorU32(ChartColors.GridLine), 3f);
            var valueText = _config.AutoScrollTimeValue.ToString();
            var valueTextSize = ImGui.CalcTextSize(valueText);
            drawList.AddText(new Vector2(valueBoxPos.X + (valueBoxWidth - valueTextSize.X) / 2, valueBoxPos.Y + (rowHeight - 4 - valueTextSize.Y) / 2), 
                ImGui.GetColorU32(ChartColors.Neutral), valueText);
            
            // Draw "+" button
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
            
            // Row 2: Unit selector buttons
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
                    _config.AutoScrollTimeUnit = (AutoScrollTimeUnit)i;
                    OnAutoScrollSettingsChanged?.Invoke(_config.AutoScrollEnabled, _config.AutoScrollTimeValue, _config.AutoScrollTimeUnit, _config.AutoScrollNowPosition);
                }
            }
            
            contentY += rowHeight;
            
            // Row 3: Position slider (where "now" appears on X-axis)
            var sliderLabelPos = new Vector2(contentX, contentY + (rowHeight - ImGui.GetTextLineHeight()) / 2);
            drawList.AddText(sliderLabelPos, ImGui.GetColorU32(ChartColors.TextSecondary), "Position:");
            
            contentY += rowHeight;
            
            // Draw the slider track
            const float sliderHeight = 8f;
            var sliderTrackPos = new Vector2(contentX, contentY + (rowHeight - sliderHeight) / 2 - 4);
            var sliderTrackWidth = drawerWidth - drawerPadding * 2;
            
            drawList.AddRectFilled(sliderTrackPos, new Vector2(sliderTrackPos.X + sliderTrackWidth, sliderTrackPos.Y + sliderHeight), 
                ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 0.8f)), 4f);
            drawList.AddRect(sliderTrackPos, new Vector2(sliderTrackPos.X + sliderTrackWidth, sliderTrackPos.Y + sliderHeight), 
                ImGui.GetColorU32(ChartColors.GridLine), 4f);
            
            // Draw filled portion
            var fillWidth = sliderTrackWidth * (_config.AutoScrollNowPosition / 100f);
            if (fillWidth > 0)
            {
                drawList.AddRectFilled(sliderTrackPos, new Vector2(sliderTrackPos.X + fillWidth, sliderTrackPos.Y + sliderHeight), 
                    ImGui.GetColorU32(new Vector4(ChartColors.Neutral.X, ChartColors.Neutral.Y, ChartColors.Neutral.Z, 0.4f)), 4f);
            }
            
            // Draw slider handle
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
            
            // Handle slider dragging
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
            
            // Draw percentage label
            var percentText = $"{_config.AutoScrollNowPosition:F0}%";
            var percentTextSize = ImGui.CalcTextSize(percentText);
            drawList.AddText(new Vector2(sliderTrackPos.X + sliderTrackWidth - percentTextSize.X, sliderTrackPos.Y + sliderHeight + 2), 
                ImGui.GetColorU32(ChartColors.TextSecondary), percentText);
        }
    }
    
    /// <summary>
    /// Toggles the visibility of a series by name.
    /// </summary>
    /// <param name="seriesName">The name of the series to toggle.</param>
    private void ToggleSeriesVisibility(string seriesName)
    {
        if (!_hiddenSeries.Add(seriesName))
        {
            _hiddenSeries.Remove(seriesName);
        }
    }

    private static Vector3[] GetSeriesColors(int count)
    {
        // Bright, vibrant color palette - easily distinguishable
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
}
