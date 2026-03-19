using System.Buffers;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Kaleidoscope.Gui.Widgets.Common;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// A reusable ImPlot graph widget for displaying numerical sample data.
/// Renders using ImPlot with a trading platform (Binance-style) aesthetic.
/// </summary>
/// <remarks>
/// This is the main graph component that orchestrates all the rendering utilities
/// from GraphDrawing, GraphLegend, and GraphControls.
/// </remarks>
public class GraphRenderer : IDisposable
{
    #region Fields
    
    private readonly GraphConfig _config;
    
    // Cached plot ID strings to avoid per-frame string interpolation
    private readonly string _plotIdSingle;
    private readonly string _plotIdMulti;
    
    /// <summary>
    /// Set of series names that are currently hidden.
    /// </summary>
    private readonly HashSet<string> _hiddenSeries = new();
    
    /// <summary>
    /// Set of group names that are currently hidden.
    /// When a group is hidden, all series belonging to that group are hidden.
    /// </summary>
    private readonly HashSet<string> _hiddenGroups = new();
    
    /// <summary>
    /// Scroll offset for the inside legend.
    /// </summary>
    private float _insideLegendScrollOffset = 0f;
    
    /// <summary>
    /// Whether the controls drawer is currently open.
    /// </summary>
    private bool _controlsDrawerOpen = false;
    
    /// <summary>
    /// Cached legend bounds from the previous frame for input blocking.
    /// </summary>
    private GraphLegend.InsideLegendResult _cachedLegendResult;
    
    /// <summary>
    /// Cached controls drawer bounds from the previous frame.
    /// </summary>
    private GraphControls.ControlsDrawerResult _cachedControlsResult;
    
    // === Array pooling ===
    private readonly Dictionary<string, double[]> _pooledXArrays = new();
    private readonly Dictionary<string, double[]> _pooledYArrays = new();
    
    // === PreparedGraphData caching ===
    private PreparedGraphData? _cachedPreparedData;
    private IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? _lastSeriesData;
    private int _lastSeriesDataCount;
    private int _lastHiddenSeriesHash;
    private bool _lastAutoScrollEnabled;
    private DateTime _lastPreparedDataTime;
    
    // === Disposed flag ===
    private bool _disposed;
    
    // === Groups ===
    private IReadOnlyList<GraphSeriesGroup>? _groups;
    
    #endregion
    
    #region Events
    
    /// <summary>
    /// Event raised when auto-scroll settings change via the controls drawer.
    /// </summary>
    public event Action<bool, int, TimeUnit, float>? OnAutoScrollSettingsChanged;
    
    #endregion
    
    #region Properties
    
    /// <summary>
    /// Gets the configuration for this graph.
    /// </summary>
    public GraphConfig Config => _config;
    
    /// <summary>
    /// Gets or sets whether the mouse is over an overlay element (legend, controls drawer).
    /// When true, plot inputs are disabled to prevent unintended panning/zooming.
    /// </summary>
    public bool IsMouseOverOverlay { get; private set; }
    
    /// <summary>
    /// Gets the set of hidden series names.
    /// </summary>
    public IReadOnlySet<string> HiddenSeries => _hiddenSeries;
    
    /// <summary>
    /// Gets the set of hidden group names.
    /// </summary>
    public IReadOnlySet<string> HiddenGroups => _hiddenGroups;
    
    /// <summary>
    /// Gets or sets the groups for the legend. Groups can be toggled to show/hide all their member series.
    /// </summary>
    public IReadOnlyList<GraphSeriesGroup>? Groups
    {
        get => _groups;
        set
        {
            _groups = value;
            // Clear cache so groups are included in next prepared data
            ClearCache();
        }
    }
    
    #endregion
    
    #region Constructors
    
    /// <summary>
    /// Creates a new GraphRenderer with default configuration.
    /// </summary>
    public GraphRenderer() : this(new GraphConfig()) { }
    
    /// <summary>
    /// Creates a new GraphRenderer with custom configuration.
    /// </summary>
    /// <param name="config">The graph configuration.</param>
    public GraphRenderer(GraphConfig config)
    {
        _config = config ?? new GraphConfig();
        _plotIdSingle = $"##{_config.PlotId}";
        _plotIdMulti = $"##{_config.PlotId}_multi";
    }
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Renders the graph with the provided samples using ImPlot.
    /// </summary>
    /// <param name="samples">The sample data to plot.</param>
    public void Render(IReadOnlyList<float> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            DrawNoDataMessage();
            return;
        }
        
        var preparedData = PrepareIndexBasedData(samples);
        DrawGraph(preparedData);
    }
    
    /// <summary>
    /// Reusable buffer for projecting 2-tuple series to 3-tuple (avoids per-frame List allocation).
    /// </summary>
    private readonly List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> _projectionBuffer = new();

    /// <summary>
    /// Renders multiple data series overlaid on the same graph with time-aligned data.
    /// Projects 2-tuple series to 3-tuple with null colors using a reusable buffer.
    /// </summary>
    /// <param name="series">List of data series with names and timestamped values.</param>
    public void RenderMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series)
    {
        _projectionBuffer.Clear();
        for (var i = 0; i < series.Count; i++)
        {
            var s = series[i];
            _projectionBuffer.Add((s.name, s.samples, null));
        }
        RenderMultipleSeries(_projectionBuffer);
    }
    
    /// <summary>
    /// Renders multiple data series with optional custom colors.
    /// </summary>
    /// <param name="series">List of data series with names, timestamped values, and optional colors.</param>
    public void RenderMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> series)
    {
        if (series == null || series.Count == 0 || series.All(s => s.samples == null || s.samples.Count == 0))
        {
            DrawNoDataMessage();
            return;
        }
        
        PreparedGraphData data;
        var needsRecompute = NeedsPreparedDataRecompute(series);
        
        if (needsRecompute)
        {
            data = PrepareTimeBasedData(series);
            _cachedPreparedData = data;
            _lastSeriesData = series;
            _lastSeriesDataCount = series.Count;
            _lastHiddenSeriesHash = ComputeHiddenSeriesHash();
            _lastAutoScrollEnabled = _config.AutoScrollEnabled;
            _lastPreparedDataTime = DateTime.UtcNow;
        }
        else
        {
            data = _cachedPreparedData!;
        }
        
        // Always update real-time limits when SimulateRealTimeUpdates is enabled
        // This extends the last data point to "now" for continuous visualization
        if (_config.SimulateRealTimeUpdates || _config.AutoScrollEnabled)
        {
            UpdateRealTimeLimits(data);
        }
        
        DrawGraph(data);
    }
    
    /// <summary>
    /// Updates the Y-axis bounds without recreating the widget.
    /// </summary>
    public void UpdateBounds(float minValue, float maxValue)
    {
        _config.MinValue = minValue;
        _config.MaxValue = maxValue;
    }
    
    /// <summary>
    /// Toggles the visibility of a series by name.
    /// </summary>
    public void ToggleSeriesVisibility(string seriesName)
    {
        if (!_hiddenSeries.Add(seriesName))
        {
            _hiddenSeries.Remove(seriesName);
        }
    }
    
    /// <summary>
    /// Shows a series by name.
    /// </summary>
    public void ShowSeries(string seriesName)
    {
        _hiddenSeries.Remove(seriesName);
    }
    
    /// <summary>
    /// Hides a series by name.
    /// </summary>
    public void HideSeries(string seriesName)
    {
        _hiddenSeries.Add(seriesName);
    }
    
    /// <summary>
    /// Checks if a series is currently hidden.
    /// </summary>
    public bool IsSeriesHidden(string seriesName) => _hiddenSeries.Contains(seriesName);
    
    /// <summary>
    /// Toggles the visibility of a group by name.
    /// When a group is hidden, all series belonging to that group are hidden.
    /// </summary>
    public void ToggleGroupVisibility(string groupName)
    {
        if (!_hiddenGroups.Add(groupName))
        {
            _hiddenGroups.Remove(groupName);
        }
    }
    
    /// <summary>
    /// Shows a group by name, making its series potentially visible
    /// (they may still be hidden if they belong to another hidden group).
    /// </summary>
    public void ShowGroup(string groupName)
    {
        _hiddenGroups.Remove(groupName);
    }
    
    /// <summary>
    /// Hides a group by name, hiding all series that belong to it.
    /// </summary>
    public void HideGroup(string groupName)
    {
        _hiddenGroups.Add(groupName);
    }
    
    /// <summary>
    /// Checks if a group is currently hidden.
    /// </summary>
    public bool IsGroupHidden(string groupName) => _hiddenGroups.Contains(groupName);
    
    /// <summary>
    /// Toggles the collapsed state of the legend.
    /// </summary>
    public void ToggleLegendCollapse()
    {
        _config.LegendCollapsed = !_config.LegendCollapsed;
    }
    
    /// <summary>
    /// Checks if a series should be visible based on both direct visibility and group visibility.
    /// A series is hidden if it's directly hidden OR if any of its groups are hidden.
    /// </summary>
    public bool IsSeriesEffectivelyHidden(GraphSeriesData series)
    {
        if (_hiddenSeries.Contains(series.Name))
            return true;
        
        if (series.GroupNames is { Count: > 0 })
        {
            foreach (var groupName in series.GroupNames)
            {
                if (_hiddenGroups.Contains(groupName))
                    return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Clears the data cache, forcing a full recompute on the next render.
    /// </summary>
    public void ClearCache()
    {
        _cachedPreparedData = null;
        _lastSeriesData = null;
    }
    
    #endregion
    
    #region Core Graph Rendering
    
    /// <summary>
    /// Core graph drawing method.
    /// </summary>
    private unsafe void DrawGraph(PreparedGraphData data)
    {
        try
        {
            var avail = ImGui.GetContentRegionAvail();
            var plotSize = new Vector2(Math.Max(1f, avail.X), Math.Max(1f, avail.Y));

            // Configure plot flags
            var plotFlags = ImPlotFlags.NoTitle | ImPlotFlags.NoLegend | ImPlotFlags.NoMenus | 
                           ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText | ImPlotFlags.Crosshairs;
            
            // Check if mouse is over overlay from previous frame
            IsMouseOverOverlay = GraphLegend.IsMouseOverLegend(_cachedLegendResult) || 
                                GraphControls.IsMouseOverDrawer(_cachedControlsResult);
            
            if (IsMouseOverOverlay)
            {
                plotFlags |= ImPlotFlags.NoInputs;
            }
            
            // Apply styling
            ChartColors.PushChartStyle(_config.Style);
            
            // Set axis limits
            var plotCondition = _config.AutoScrollEnabled ? ImPlotCond.Always : ImPlotCond.Once;
            ImPlot.SetNextAxesLimits(data.XMin, data.XMax, data.YMin, data.YMax, plotCondition);

            var plotId = data.HasMultipleSeries ? _plotIdMulti : _plotIdSingle;
            
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
                    ImPlot.SetupAxisFormat(ImAxis.X1, GraphDrawing.XAxisTimeFormatter, (void*)data.StartTime.Ticks);
                }
                ImPlot.SetupAxisFormat(ImAxis.Y1, GraphDrawing.YAxisFormatter);
                
                // Constrain axes
                ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, 0, double.MaxValue);
                ImPlot.SetupAxisLimitsConstraints(ImAxis.Y1, 0, double.MaxValue);
                
                // Plot dummy points for auto-fit padding
                var dummyX = stackalloc double[2] { 0, data.XMax };
                var dummyY = stackalloc double[2] { data.YMin > 0 ? data.YMin : 0, data.YMax };
                ImPlot.SetNextMarkerStyle(ImPlotMarker.None);
                ImPlot.SetNextLineStyle(new Vector4(0, 0, 0, 0), 0);
                ImPlot.PlotLine("##padding", dummyX, dummyY, 2);
                
                // Draw each series (check both direct visibility and group visibility)
                foreach (var series in data.Series)
                {
                    if (!series.Visible || IsSeriesEffectivelyHidden(series)) continue;
                    DrawSeries(series, data);
                }
                
                // Draw current price line for single series
                if (_config.ShowCurrentPriceLine && !data.HasMultipleSeries)
                {
                    var lastVisibleSeries = data.Series.LastOrDefault(s => s.Visible && !IsSeriesEffectivelyHidden(s));
                    if (lastVisibleSeries != null && lastVisibleSeries.PointCount > 0)
                    {
                        var currentValue = lastVisibleSeries.YValues[lastVisibleSeries.PointCount - 1];
                        GraphDrawing.DrawCurrentPriceLine(currentValue, _config.Style);
                    }
                }
                
                // Draw value labels first to get their bounds for hover detection
                List<GraphValueLabels.ValueLabelBounds>? valueLabelBounds = null;
                var isHoveringValueLabel = false;
                if (_config.ShowValueLabel)
                {
                    valueLabelBounds = DrawValueLabels(data);
                    
                    // Check if hovering a value label
                    if (valueLabelBounds is { Count: > 0 })
                    {
                        var mousePos = ImGui.GetMousePos();
                        foreach (var labelBounds in valueLabelBounds)
                        {
                            if (labelBounds.Contains(mousePos))
                            {
                                isHoveringValueLabel = true;
                                break;
                            }
                        }
                    }
                }
                
                // Draw hover effects (only if not hovering a value label)
                if (_config.ShowCrosshair && ImPlot.IsPlotHovered() && !isHoveringValueLabel)
                {
                    DrawHoverEffects(data);
                }
                
                // Show value label tooltip if hovering one
                if (isHoveringValueLabel && valueLabelBounds != null)
                {
                    var mousePos = ImGui.GetMousePos();
                    foreach (var labelBounds in valueLabelBounds)
                    {
                        if (labelBounds.Contains(mousePos))
                        {
                            var lines = new List<string> { $"{labelBounds.SeriesName}: {NumberFormatter.Format(labelBounds.Value, _config.NumberFormat)}" };
                            var color = new Vector4(labelBounds.Color.X, labelBounds.Color.Y, labelBounds.Color.Z, 1f);
                            GraphTooltips.DrawTooltipBox(mousePos, lines.ToArray(), color, _config.Style);
                            break;
                        }
                    }
                }
                
                // Draw inside legend if applicable
                if (_config.ShowLegend && _config.LegendPosition != LegendPosition.Outside && data.HasMultipleSeriesTotal)
                {
                    _cachedLegendResult = GraphLegend.DrawInsideLegend(
                        data,
                        _hiddenSeries,
                        _hiddenGroups,
                        _config.LegendPosition,
                        _config.LegendHeightPercent,
                        _insideLegendScrollOffset,
                        _config.LegendCollapsed,
                        ToggleSeriesVisibility,
                        ToggleGroupVisibility,
                        ToggleLegendCollapse,
                        _config.Style);
                    _insideLegendScrollOffset = _cachedLegendResult.ScrollOffset;
                    // Note: CollapseToggled is handled via the ToggleLegendCollapse callback
                }
                else
                {
                    _cachedLegendResult = GraphLegend.InsideLegendResult.Invalid;
                }
                
                // Draw controls drawer
                if (_config.ShowControlsDrawer)
                {
                    _cachedControlsResult = GraphControls.DrawControlsDrawer(
                        _controlsDrawerOpen,
                        _config.AutoScrollEnabled,
                        _config.AutoScrollTimeValue,
                        _config.AutoScrollTimeUnit,
                        _config.AutoScrollNowPosition,
                        _config.Style);
                    
                    // Update state from result
                    _controlsDrawerOpen = _cachedControlsResult.IsOpen;
                    if (_cachedControlsResult.SettingsChanged)
                    {
                        _config.AutoScrollEnabled = _cachedControlsResult.AutoScrollEnabled;
                        _config.AutoScrollTimeValue = _cachedControlsResult.AutoScrollTimeValue;
                        _config.AutoScrollTimeUnit = _cachedControlsResult.AutoScrollTimeUnit;
                        _config.AutoScrollNowPosition = _cachedControlsResult.AutoScrollNowPosition;
                        
                        OnAutoScrollSettingsChanged?.Invoke(
                            _config.AutoScrollEnabled,
                            _config.AutoScrollTimeValue,
                            _config.AutoScrollTimeUnit,
                            _config.AutoScrollNowPosition);
                    }
                }
                else
                {
                    _cachedControlsResult = GraphControls.ControlsDrawerResult.Invalid;
                }
                
                ImPlot.EndPlot();
            }
            
            ChartColors.PopChartStyle();
        }
        catch (Exception ex)
        {
            ImGui.TextUnformatted($"Error rendering graph: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Draws a single series on the plot.
    /// </summary>
    private unsafe void DrawSeries(GraphSeriesData series, PreparedGraphData data)
    {
        var colorVec4 = new Vector4(series.Color.X, series.Color.Y, series.Color.Z, 1f);
        ImPlot.SetNextLineStyle(colorVec4, _config.Style.LineWeight);
        
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
                        ? data.TotalTimeSpan / count * 0.8 / data.VisibleSeriesCount
                        : 0.67;
                    ImPlot.SetNextFillStyle(colorVec4);
                    ImPlot.PlotBars(series.Name, xPtr, yPtr, count, barWidth);
                    break;
                    
                case GraphType.StairsArea:
                    DrawStairsArea(series, data, colorVec4, xPtr, yPtr, count);
                    break;
                    
                case GraphType.Area:
                default:
                    var fillAlpha = data.HasMultipleSeries ? _config.Style.MultiSeriesFillAlpha : _config.Style.FillAlpha + 0.25f;
                    ImPlot.SetNextFillStyle(new Vector4(series.Color.X, series.Color.Y, series.Color.Z, fillAlpha));
                    ImPlot.PlotShaded($"{series.Name}##shaded", xPtr, yPtr, count, 0.0);
                    ImPlot.SetNextLineStyle(colorVec4, _config.Style.LineWeight);
                    ImPlot.PlotLine(series.Name, xPtr, yPtr, count);
                    break;
            }
        }
    }
    
    /// <summary>
    /// Draws a stairs/step chart with filled area below the line.
    /// Creates step-shaped data for the shaded region to match the stairs line.
    /// </summary>
    private unsafe void DrawStairsArea(GraphSeriesData series, PreparedGraphData data, Vector4 colorVec4, double* xPtr, double* yPtr, int count)
    {
        if (count < 2)
        {
            ImPlot.PlotStairs(series.Name, xPtr, yPtr, count);
            return;
        }
        
        // Create expanded arrays for stair-shaped shading
        // Each original point becomes 2 points (except the last one)
        // Pattern: (x0,y0), (x1,y0), (x1,y1), (x2,y1), (x2,y2), ...
        var expandedCount = count * 2 - 1;
        var expandedX = ArrayPool<double>.Shared.Rent(expandedCount);
        var expandedY = ArrayPool<double>.Shared.Rent(expandedCount);
        
        try
        {
            for (var i = 0; i < count - 1; i++)
            {
                var idx = i * 2;
                expandedX[idx] = xPtr[i];
                expandedY[idx] = yPtr[i];
                expandedX[idx + 1] = xPtr[i + 1];
                expandedY[idx + 1] = yPtr[i];  // Horizontal step to next X at current Y
            }
            // Add final point
            expandedX[expandedCount - 1] = xPtr[count - 1];
            expandedY[expandedCount - 1] = yPtr[count - 1];
            
            var fillAlpha = data.HasMultipleSeries ? _config.Style.MultiSeriesFillAlpha : _config.Style.FillAlpha + 0.25f;
            
            fixed (double* expXPtr = expandedX)
            fixed (double* expYPtr = expandedY)
            {
                ImPlot.SetNextFillStyle(new Vector4(series.Color.X, series.Color.Y, series.Color.Z, fillAlpha));
                ImPlot.PlotShaded($"{series.Name}##shaded", expXPtr, expYPtr, expandedCount, 0.0);
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(expandedX);
            ArrayPool<double>.Shared.Return(expandedY);
        }
        
        ImPlot.SetNextLineStyle(colorVec4, _config.Style.LineWeight);
        ImPlot.PlotStairs(series.Name, xPtr, yPtr, count);
    }
    
    /// <summary>
    /// Draws crosshair and tooltip when hovering over the plot.
    /// Only shows tooltip when hovering near a series line or within a series area.
    /// </summary>
    private void DrawHoverEffects(PreparedGraphData data)
    {
        var mousePos = ImPlot.GetPlotMousePos();
        var mouseX = mousePos.X;
        var mouseY = mousePos.Y;
        
        // Find nearest point across all visible series, checking if we're actually over the series
        string nearestSeriesName = string.Empty;
        float nearestValue = 0f;
        var nearestColor = new Vector3(1f, 1f, 1f);
        var foundHoveredSeries = false;
        var minYDistance = double.MaxValue;
        
        // Define pixel threshold for "hovering near line"
        var lineHoverThresholdPixels = _config.Style.LineWeight * 3 + 4f;
        
        foreach (var series in data.Series)
        {
            if (!series.Visible || IsSeriesEffectivelyHidden(series) || series.PointCount == 0) continue;
            
            var idx = BinarySearchNearestX(series.XValues, series.PointCount, mouseX);
            
            if (idx >= 0 && idx < series.PointCount)
            {
                var value = (float)series.YValues[idx];
                var isHoveringThisSeries = false;
                
                // Get the X range of this series
                var seriesMinX = series.XValues[0];
                var seriesMaxX = series.XValues[series.PointCount - 1];
                
                // Check if mouse is over this series based on graph type
                if (_config.GraphType == GraphType.Area || _config.GraphType == GraphType.StairsArea)
                {
                    // For area charts: check if mouse X is within the series data range
                    // and mouse Y is between 0 and the series value (within the filled area)
                    if (mouseX >= seriesMinX && mouseX <= seriesMaxX)
                    {
                        var seriesValueAtMouse = GetSeriesValueAtX(series, mouseX);
                        isHoveringThisSeries = mouseY >= 0 && mouseY <= seriesValueAtMouse;
                        
                        // Also check if near the line itself
                        if (!isHoveringThisSeries)
                        {
                            isHoveringThisSeries = IsNearSeriesLine(series, mouseX, mouseY, lineHoverThresholdPixels);
                        }
                    }
                }
                else
                {
                    // For line/stairs/bars: check if mouse is near the line
                    isHoveringThisSeries = IsNearSeriesLine(series, mouseX, mouseY, lineHoverThresholdPixels);
                }
                
                if (isHoveringThisSeries)
                {
                    var yDistance = Math.Abs(mouseY - value);
                    if (yDistance < minYDistance)
                    {
                        minYDistance = yDistance;
                        nearestSeriesName = series.Name;
                        nearestValue = value;
                        nearestColor = series.Color;
                        foundHoveredSeries = true;
                    }
                }
            }
        }
        
        if (foundHoveredSeries)
        {
            GraphDrawing.DrawCrosshair(mouseX, mouseY, nearestValue, _config.Style, _config.NumberFormat);
            
            // Draw tooltip for multi-series
            if (data.HasMultipleSeries)
            {
                var screenPos = ImPlot.PlotToPixels(mouseX, mouseY);
                var lines = new List<string>();
                
                // Add time if time-based
                if (data.IsTimeBased)
                {
                    var time = data.StartTime.AddSeconds(mouseX).ToLocalTime();
                    lines.Add(time.ToString("g"));
                }
                
                // Add value for nearest series
                lines.Add($"{nearestSeriesName}: {NumberFormatter.Format(nearestValue, _config.NumberFormat)}");
                
                GraphTooltips.DrawTooltipBox(screenPos, lines.ToArray(), new Vector4(nearestColor.X, nearestColor.Y, nearestColor.Z, 1f), _config.Style);
            }
        }
    }
    
    /// <summary>
    /// Gets the Y value of a series at a given X position, interpolating for stairs/area charts.
    /// </summary>
    private static double GetSeriesValueAtX(GraphSeriesData series, double x)
    {
        if (series.PointCount == 0) return 0;
        if (series.PointCount == 1) return series.YValues[0];
        
        // Find the segment containing x
        for (var i = 0; i < series.PointCount - 1; i++)
        {
            if (x >= series.XValues[i] && x < series.XValues[i + 1])
            {
                // For stairs, return the current segment's Y value
                return series.YValues[i];
            }
        }
        
        // If x is at or beyond the last point, return the last value
        if (x >= series.XValues[series.PointCount - 1])
            return series.YValues[series.PointCount - 1];
        
        // If x is before the first point, return the first value
        return series.YValues[0];
    }
    
    /// <summary>
    /// Checks if the mouse position is near the series line within a pixel threshold.
    /// </summary>
    private bool IsNearSeriesLine(GraphSeriesData series, double mouseX, double mouseY, float thresholdPixels)
    {
        if (series.PointCount == 0) return false;
        
        // Get the Y value at the mouse X position
        var idx = BinarySearchNearestX(series.XValues, series.PointCount, mouseX);
        if (idx < 0 || idx >= series.PointCount) return false;
        
        // For stairs charts, use step behavior
        double seriesY;
        if (_config.GraphType == GraphType.Stairs || _config.GraphType == GraphType.StairsArea)
        {
            seriesY = GetSeriesValueAtX(series, mouseX);
        }
        else
        {
            // For line charts, interpolate between points if in between
            if (idx < series.PointCount - 1 && mouseX > series.XValues[idx] && mouseX < series.XValues[idx + 1])
            {
                var x0 = series.XValues[idx];
                var x1 = series.XValues[idx + 1];
                var y0 = series.YValues[idx];
                var y1 = series.YValues[idx + 1];
                var t = (mouseX - x0) / (x1 - x0);
                seriesY = y0 + t * (y1 - y0);
            }
            else
            {
                seriesY = series.YValues[idx];
            }
        }
        
        // Convert to pixel coordinates and check distance
        var mousePixel = ImPlot.PlotToPixels(mouseX, mouseY);
        var seriesPixel = ImPlot.PlotToPixels(mouseX, seriesY);
        var pixelDistance = Math.Abs(mousePixel.Y - seriesPixel.Y);
        
        return pixelDistance <= thresholdPixels;
    }
    
    /// <summary>
    /// Draws current value labels at the latest point of each visible series.
    /// Labels are colored to match their series and auto-adjust to prevent overlap.
    /// </summary>
    /// <returns>List of label bounds for hover detection.</returns>
    private List<GraphValueLabels.ValueLabelBounds> DrawValueLabels(PreparedGraphData data)
    {
        var seriesData = new List<(string Name, double LastX, double LastY, Vector3 Color)>();
        
        foreach (var series in data.Series)
        {
            if (!series.Visible || IsSeriesEffectivelyHidden(series) || series.PointCount == 0) continue;
            
            var lastX = series.XValues[series.PointCount - 1];
            var lastY = series.YValues[series.PointCount - 1];
            seriesData.Add((series.Name, lastX, lastY, series.Color));
        }
        
        if (seriesData.Count > 0)
        {
            return GraphValueLabels.DrawCurrentValueLabels(seriesData, _config.Style, _config.ValueLabelOffsetX, _config.ValueLabelOffsetY, _config.NumberFormat);
        }
        
        return new List<GraphValueLabels.ValueLabelBounds>();
    }
    
    /// <summary>
    /// Displays the "no data" message.
    /// </summary>
    private void DrawNoDataMessage()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, _config.Style.Colors.TextSecondary);
        ImGui.TextUnformatted(_config.NoDataText);
        ImGui.PopStyleColor();
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
        
        var isBullish = samples.Count < 2 || samples[^1] >= samples[0];
        var colors = _config.Style.Colors;
        var color = isBullish 
            ? new Vector3(colors.Bullish.X, colors.Bullish.Y, colors.Bullish.Z)
            : new Vector3(colors.Bearish.X, colors.Bearish.Y, colors.Bearish.Z);
        
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
        
        var (yMin, yMax) = CalculateYBounds(series, 0, double.MaxValue);
        var xDataMax = (double)samples.Count;
        var xPadding = Math.Max(xDataMax * 0.05, 1.0);
        
        return new PreparedGraphData
        {
            Series = series,
            Groups = _groups,
            XMin = 0,
            XMax = xDataMax + xPadding,
            YMin = yMin,
            YMax = yMax,
            IsTimeBased = false,
            StartTime = DateTime.MinValue,
            TotalTimeSpan = xDataMax
        };
        // Note: single-series index-based data always has 1 visible series.
        // UpdateVisibleSeriesCount is called after return by the caller's Render path
        // which uses DrawGraph (the only consumer of HasMultipleSeries).
    }
    
    /// <summary>
    /// Prepares time-based multi-series data for rendering.
    /// </summary>
    /// <summary>
    /// Prepares time-based multi-series data with optional custom colors.
    /// When a series has no custom color, a default palette color is assigned.
    /// </summary>
    private PreparedGraphData PrepareTimeBasedData(
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> seriesData)
    {
        var globalMinTime = DateTime.MaxValue;
        var globalMaxTime = DateTime.UtcNow;
        
        foreach (var (_, samples, _) in seriesData)
        {
            if (samples == null || samples.Count == 0) continue;
            if (samples[0].ts < globalMinTime) globalMinTime = samples[0].ts;
        }
        
        if (globalMinTime == DateTime.MaxValue)
            globalMinTime = DateTime.UtcNow.AddHours(-1);
        
        var totalTimeSpan = Math.Max(1, (globalMaxTime - globalMinTime).TotalSeconds);
        var defaultColors = ChartColors.GetSeriesColors(seriesData.Count, _config.Style.Colors);
        
        var series = new List<GraphSeriesData>();
        var defaultColorIndex = 0;
        
        for (var i = 0; i < seriesData.Count; i++)
        {
            var (name, samples, customColor) = seriesData[i];
            if (samples == null || samples.Count == 0) continue;
            
            var pointCount = samples.Count + 1;
            var xValues = GetOrCreatePooledArray(_pooledXArrays, name, pointCount);
            var yValues = GetOrCreatePooledArray(_pooledYArrays, name, pointCount);
            
            for (var j = 0; j < samples.Count; j++)
            {
                xValues[j] = (samples[j].ts - globalMinTime).TotalSeconds;
                yValues[j] = samples[j].value;
            }
            
            xValues[samples.Count] = totalTimeSpan;
            yValues[samples.Count] = samples[^1].value;
            
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
                Visible = !_hiddenSeries.Contains(name),
                GroupNames = GetGroupNamesForSeries(name)
            });
        }
        
        var (xMin, xMax) = CalculateXLimits(totalTimeSpan);
        var (yMin, yMax) = CalculateYBounds(series, xMin, xMax);
        
        var result = new PreparedGraphData
        {
            Series = series,
            Groups = _groups,
            XMin = xMin,
            XMax = xMax,
            YMin = yMin,
            YMax = yMax,
            IsTimeBased = true,
            StartTime = globalMinTime,
            TotalTimeSpan = totalTimeSpan
        };
        result.UpdateVisibleSeriesCount();
        return result;
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Gets the list of group names that a series belongs to, based on the configured groups.
    /// </summary>
    private IReadOnlyList<string>? GetGroupNamesForSeries(string seriesName)
    {
        if (_groups == null || _groups.Count == 0)
            return null;
        
        var groupNames = new List<string>();
        foreach (var group in _groups)
        {
            if (group.SeriesNames.Contains(seriesName))
            {
                groupNames.Add(group.Name);
            }
        }
        
        return groupNames.Count > 0 ? groupNames : null;
    }
    
    private (double xMin, double xMax) CalculateXLimits(double totalTimeSpan)
    {
        if (_config.AutoScrollEnabled)
        {
            var timeRangeSeconds = _config.GetAutoScrollTimeRangeSeconds();
            var nowFraction = _config.AutoScrollNowPosition / 100f;
            var leftPortion = timeRangeSeconds * nowFraction;
            var rightPortion = timeRangeSeconds * (1f - nowFraction);
            return (totalTimeSpan - leftPortion, totalTimeSpan + rightPortion);
        }
        
        return (0, totalTimeSpan + Math.Max(totalTimeSpan * 0.05, 1.0));
    }
    
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
    
private void UpdateRealTimeLimits(PreparedGraphData data)
    {
        // Update TotalTimeSpan to reflect current time for real-time visualization
        // This extends the last data point to "now", making the graph appear to continuously update
        if (data.IsTimeBased && data.StartTime != DateTime.MinValue)
        {
            var newTotalTimeSpan = Math.Max(1, (DateTime.UtcNow - data.StartTime).TotalSeconds);
            
            // Update the synthetic "now" point in each series to extend to current time
            foreach (var series in data.Series)
            {
                if (series.PointCount > 0)
                {
                    // The last point is the synthetic "now" point - update its X value
                    series.XValues[series.PointCount - 1] = newTotalTimeSpan;
                }
            }

            data.TotalTimeSpan = newTotalTimeSpan;
        }
        
        // Recalculate X limits based on auto-scroll settings or default behavior
        var (xMin, xMax) = CalculateXLimits(data.TotalTimeSpan);
        data.XMin = xMin;
        data.XMax = xMax;
        
        var (yMin, yMax) = CalculateYBounds(data.Series, xMin, xMax);
        data.YMin = yMin;
        data.YMax = yMax;
    }
    
    private bool NeedsPreparedDataRecompute(
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> series)
    {
        if (_cachedPreparedData == null || _lastSeriesData == null)
            return true;
        
        if (!ReferenceEquals(series, _lastSeriesData))
            return true;
        
        // Secondary check: detect in-place mutations via count change
        if (series.Count != _lastSeriesDataCount)
            return true;
        
        var currentHiddenHash = ComputeHiddenSeriesHash();
        if (currentHiddenHash != _lastHiddenSeriesHash)
            return true;
        
        if (_config.AutoScrollEnabled != _lastAutoScrollEnabled)
            return true;
        
        return false;
    }
    
    private int ComputeHiddenSeriesHash()
    {
        // Use XOR for order-independent hashing — HashSet iteration order is non-deterministic
        var hash = _hiddenSeries.Count;
        foreach (var name in _hiddenSeries)
        {
            hash ^= name.GetHashCode();
        }
        // Also include hidden groups
        hash ^= _hiddenGroups.Count << 16;
        foreach (var name in _hiddenGroups)
        {
            hash ^= name.GetHashCode();
        }
        return hash;
    }
    
    private static double[] GetOrCreatePooledArray(Dictionary<string, double[]> pool, string key, int requiredSize)
    {
        if (pool.TryGetValue(key, out var existing) && existing.Length >= requiredSize)
        {
            return existing;
        }
        
        var newArray = new double[requiredSize + 16];
        pool[key] = newArray;
        return newArray;
    }
    
    private static int BinarySearchNearestX(double[] xValues, int count, double target)
    {
        if (count == 0) return -1;
        
        if (target < xValues[0]) return -1;
        if (target >= xValues[count - 1]) return count - 1;
        
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
    
    #region IDisposable
    
    /// <summary>
    /// Releases pooled arrays and clears cached data.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _pooledXArrays.Clear();
        _pooledYArrays.Clear();
        _cachedPreparedData = null;
        _lastSeriesData = null;
    }
    
    #endregion
}
