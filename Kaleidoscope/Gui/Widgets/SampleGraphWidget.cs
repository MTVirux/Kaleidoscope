using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;
using System.Globalization;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable graph widget for displaying numerical sample data.
/// Renders a PlotLines graph that fills available space with tooltips showing values.
/// </summary>
public class SampleGraphWidget
{
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
        /// The ID suffix for the ImGui child and plot elements.
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
        /// Whether to show the latest value at the end of the line.
        /// </summary>
        public bool ShowLatestValue { get; set; } = false;

        /// <summary>
        /// Whether to leave a gap at the right edge of the graph.
        /// </summary>
        public bool ShowEndGap { get; set; } = false;

        /// <summary>
        /// Percentage of graph width to use as end gap.
        /// </summary>
        public float EndGapPercent { get; set; } = 5f;

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
        /// Whether to auto-scale the Y-axis based on actual data values.
        /// </summary>
        public bool AutoScaleGraph { get; set; } = true;
    }

    private readonly GraphConfig _config;
    
    // Zoom state for CTRL+scroll
    private float _zoomLevel = 1.0f;
    private float _zoomCenterX = 0.5f; // 0-1 normalized position
    
    // Ticker animation state
    private float _tickerOffset = 0f;
    private DateTime _lastTickerUpdate = DateTime.MinValue;
    private const float TickerSpeed = 30f; // pixels per second

    /// <summary>
    /// Creates a new SampleGraphWidget with default configuration.
    /// </summary>
    public SampleGraphWidget() : this(new GraphConfig()) { }

    /// <summary>
    /// Creates a new SampleGraphWidget with custom configuration.
    /// </summary>
    /// <param name="config">The graph configuration.</param>
    public SampleGraphWidget(GraphConfig config)
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
    public void UpdateDisplayOptions(bool showLatestValue, bool showEndGap, float endGapPercent, bool showValueLabel, float valueLabelOffsetX = 0f, float valueLabelOffsetY = 0f, bool autoScaleGraph = true)
    {
        _config.ShowLatestValue = showLatestValue;
        _config.ShowEndGap = showEndGap;
        _config.EndGapPercent = endGapPercent;
        _config.ShowValueLabel = showValueLabel;
        _config.ValueLabelOffsetX = valueLabelOffsetX;
        _config.ValueLabelOffsetY = valueLabelOffsetY;
        _config.AutoScaleGraph = autoScaleGraph;
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
    /// Draws the graph with the provided samples.
    /// </summary>
    /// <param name="samples">The sample data to plot.</param>
    public void Draw(IReadOnlyList<float> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }

        var arr = PrepareDataArray(samples);
        var min = _config.MinValue;
        var max = _config.MaxValue;

        // Ensure we have a non-zero vertical range for plotting
        if (Math.Abs(max - min) < _config.FloatEpsilon)
        {
            max = min + 1f;
        }

        try
        {
            var avail = ImGui.GetContentRegionAvail();
            
            // Reserve space for latest value label on the right if enabled
            var reservedWidth = 0f;
            if (_config.ShowLatestValue && samples.Count > 0)
            {
                reservedWidth = ImGui.CalcTextSize(FormatValue(samples[^1])).X + 10f;
            }

            var graphWidth = Math.Max(1f, avail.X - reservedWidth);
            var graphHeight = Math.Max(1f, avail.Y);

            // Get plot position before drawing
            var plotPos = ImGui.GetCursorScreenPos();
            var plotSize = new Vector2(graphWidth, graphHeight);

            ImGui.BeginChild($"{_config.PlotId}_child", plotSize, false);

            var childAvailAfterBegin = ImGui.GetContentRegionAvail();
            ImGui.SetNextItemWidth(Math.Max(1f, childAvailAfterBegin.X));

            var innerPlotSize = new Vector2(
                Math.Max(1f, childAvailAfterBegin.X),
                Math.Max(1f, childAvailAfterBegin.Y));

            // Get actual plot position inside child
            var innerPlotPos = ImGui.GetCursorScreenPos();

            ImGui.PlotLines($"##{_config.PlotId}", arr, arr.Length, "", min, max, innerPlotSize);

            // Draw value label if enabled (inside the plot area)
            if (_config.ShowValueLabel && samples.Count > 0)
            {
                DrawValueLabel(samples[^1], innerPlotPos, innerPlotSize, min, max);
            }

            ImGui.EndChild();

            // Show tooltip with value when hovering
            DrawTooltip(samples is float[] arrCast ? arrCast : samples.ToArray());

            // Draw latest value at line end if enabled (to the right of the graph)
            if (_config.ShowLatestValue && samples.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted(FormatValue(samples[^1]));
            }
        }
        catch (Exception ex)
        {
            // Fall back to default small plot if anything goes wrong
            LogService.Debug($"[SampleGraphWidget] Graph rendering error: {ex.Message}");
            ImGui.PlotLines($"##{_config.PlotId}", arr, arr.Length);
        }
    }

    /// <summary>
    /// Draws multiple data series overlaid on the same graph with time-aligned data.
    /// All lines extend to current time using their last recorded value.
    /// Uses ImGui DrawList to render multiple lines on a single plot area.
    /// Supports CTRL+scroll to zoom and scrolling ticker for legend.
    /// </summary>
    /// <param name="series">List of data series with names and timestamped values.</param>
    public void DrawMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series)
    {
        if (series == null || series.Count == 0)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }

        // Find global time range across all series
        var globalMinTime = DateTime.MaxValue;
        var globalMaxTime = DateTime.Now; // Use current time as max so all lines end at same point

        foreach (var (_, samples) in series)
        {
            if (samples == null || samples.Count == 0) continue;
            if (samples[0].ts < globalMinTime) globalMinTime = samples[0].ts;
        }

        if (globalMinTime == DateTime.MaxValue)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }

        var totalTimeSpan = (globalMaxTime - globalMinTime).TotalSeconds;
        if (totalTimeSpan < 1) totalTimeSpan = 1;

        // Apply end gap
        if (_config.ShowEndGap)
        {
            var gapPercent = Math.Clamp(_config.EndGapPercent, 0f, 50f);
            totalTimeSpan *= (1f + gapPercent / 100f);
        }

        // Calculate Y-axis bounds
        float yMin, yMax;
        if (_config.AutoScaleGraph)
        {
            var dataMin = float.MaxValue;
            var dataMax = float.MinValue;
            foreach (var (_, samples) in series)
            {
                if (samples == null) continue;
                foreach (var (_, val) in samples)
                {
                    if (val < dataMin) dataMin = val;
                    if (val > dataMax) dataMax = val;
                }
            }

            var dataRange = dataMax - dataMin;
            if (dataRange < _config.FloatEpsilon)
            {
                dataRange = Math.Max(dataMax * 0.1f, 1f);
            }
            yMin = Math.Max(0f, dataMin - dataRange * 0.1f);
            yMax = dataMax + dataRange * 0.1f;
        }
        else
        {
            yMin = _config.MinValue;
            yMax = _config.MaxValue;
            if (Math.Abs(yMax - yMin) < _config.FloatEpsilon)
            {
                yMax = yMin + 1f;
            }
        }

        try
        {
            var avail = ImGui.GetContentRegionAvail();
            var graphWidth = Math.Max(1f, avail.X);
            var legendHeight = 20f;
            var graphHeight = Math.Max(1f, avail.Y - legendHeight);

            // Draw scrolling ticker legend at top
            DrawTickerLegend(series, graphWidth, legendHeight);

            // Get plot area position
            var plotPos = ImGui.GetCursorScreenPos();
            var plotSize = new Vector2(graphWidth, graphHeight);

            // Draw background rect for the plot area
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), 
                ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.5f)));

            // Handle CTRL+scroll zoom
            var plotRect = new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y);
            var isHovered = ImGui.IsMouseHoveringRect(plotPos, plotRect);
            
            if (isHovered)
            {
                var io = ImGui.GetIO();
                var wheel = io.MouseWheel;
                
                if (io.KeyCtrl && Math.Abs(wheel) > 0.01f)
                {
                    // Calculate mouse position relative to plot (0-1)
                    var mouseRelX = (io.MousePos.X - plotPos.X) / plotSize.X;
                    mouseRelX = Math.Clamp(mouseRelX, 0f, 1f);
                    
                    // Zoom in/out
                    var zoomFactor = wheel > 0 ? 1.2f : 1f / 1.2f;
                    var newZoom = Math.Clamp(_zoomLevel * zoomFactor, 1f, 20f);
                    
                    if (Math.Abs(newZoom - _zoomLevel) > 0.001f)
                    {
                        // Adjust center to keep mouse position stable
                        var oldViewStart = _zoomCenterX - 0.5f / _zoomLevel;
                        var mouseWorldX = oldViewStart + mouseRelX / _zoomLevel;
                        
                        _zoomLevel = newZoom;
                        
                        // Calculate new center so mouse stays at same world position
                        var newViewStart = mouseWorldX - mouseRelX / _zoomLevel;
                        _zoomCenterX = newViewStart + 0.5f / _zoomLevel;
                        
                        // Clamp center to valid range
                        var halfView = 0.5f / _zoomLevel;
                        _zoomCenterX = Math.Clamp(_zoomCenterX, halfView, 1f - halfView);
                    }
                }
            }

            // Calculate visible time range based on zoom
            var viewStart = _zoomCenterX - 0.5f / _zoomLevel;
            var viewEnd = _zoomCenterX + 0.5f / _zoomLevel;
            viewStart = Math.Clamp(viewStart, 0f, 1f);
            viewEnd = Math.Clamp(viewEnd, 0f, 1f);

            var visibleStartTime = globalMinTime.AddSeconds(viewStart * totalTimeSpan);
            var visibleEndTime = globalMinTime.AddSeconds(viewEnd * totalTimeSpan);
            var visibleTimeSpan = (visibleEndTime - visibleStartTime).TotalSeconds;
            if (visibleTimeSpan < 1) visibleTimeSpan = 1;

            // Draw each series as a polyline, extending to current time
            var legendColors = GetSeriesColors(series.Count);
            for (var seriesIdx = 0; seriesIdx < series.Count; seriesIdx++)
            {
                var (_, samples) = series[seriesIdx];
                if (samples == null || samples.Count == 0) continue;

                var color = legendColors[seriesIdx];
                var colorU32 = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 1f));

                // Draw lines between data points
                for (var i = 0; i < samples.Count - 1; i++)
                {
                    var t1 = (samples[i].ts - visibleStartTime).TotalSeconds / visibleTimeSpan;
                    var t2 = (samples[i + 1].ts - visibleStartTime).TotalSeconds / visibleTimeSpan;
                    
                    // Skip if both points are outside visible range
                    if ((t1 < 0 && t2 < 0) || (t1 > 1 && t2 > 1)) continue;
                    
                    var x1 = plotPos.X + (float)t1 * plotSize.X;
                    var x2 = plotPos.X + (float)t2 * plotSize.X;
                    
                    var y1Normalized = (samples[i].value - yMin) / (yMax - yMin);
                    var y2Normalized = (samples[i + 1].value - yMin) / (yMax - yMin);
                    
                    var y1 = plotPos.Y + plotSize.Y - (y1Normalized * plotSize.Y);
                    var y2 = plotPos.Y + plotSize.Y - (y2Normalized * plotSize.Y);

                    drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), colorU32, 1.5f);
                }

                // Extend last point to current time (all lines end at same vertical position)
                if (samples.Count > 0)
                {
                    var lastSample = samples[^1];
                    var tLast = (lastSample.ts - visibleStartTime).TotalSeconds / visibleTimeSpan;
                    var tNow = (globalMaxTime - visibleStartTime).TotalSeconds / visibleTimeSpan;
                    
                    if (tLast < 1 && tNow > 0 && tNow > tLast)
                    {
                        var xLast = plotPos.X + (float)tLast * plotSize.X;
                        var xNow = plotPos.X + (float)Math.Min(tNow, 1.0) * plotSize.X;
                        
                        var yNormalized = (lastSample.value - yMin) / (yMax - yMin);
                        var yPos = plotPos.Y + plotSize.Y - (yNormalized * plotSize.Y);

                        // Draw horizontal line from last point to current time
                        drawList.AddLine(new Vector2(xLast, yPos), new Vector2(xNow, yPos), colorU32, 1.5f);
                    }
                }
            }

            // Draw zoom indicator if zoomed in
            if (_zoomLevel > 1.01f)
            {
                var zoomText = $"{_zoomLevel:0.0}x";
                var textSize = ImGui.CalcTextSize(zoomText);
                drawList.AddText(
                    new Vector2(plotPos.X + plotSize.X - textSize.X - 5, plotPos.Y + 5),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.6f)),
                    zoomText);
            }

            // Advance cursor past the plot area
            ImGui.Dummy(plotSize);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[SampleGraphWidget] Multi-series rendering error: {ex.Message}");
            ImGui.TextUnformatted("Error rendering graph");
        }
    }

    /// <summary>
    /// Draws a scrolling ticker-style legend showing character names and values.
    /// </summary>
    private void DrawTickerLegend(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series, float width, float height)
    {
        var legendColors = GetSeriesColors(series.Count);
        
        // Build the full ticker text
        var tickerParts = new List<(string text, Vector4 color)>();
        var totalTextWidth = 0f;
        var separator = "  •  ";
        var separatorWidth = ImGui.CalcTextSize(separator).X;
        
        for (var i = 0; i < series.Count; i++)
        {
            var (name, samples) = series[i];
            var color = legendColors[i];
            var latestVal = samples != null && samples.Count > 0 ? FormatValue(samples[^1].value) : "N/A";
            var text = $"{name}: {latestVal}";
            var textWidth = ImGui.CalcTextSize(text).X;
            
            tickerParts.Add((text, new Vector4(color.X, color.Y, color.Z, 1f)));
            totalTextWidth += textWidth;
            
            if (i < series.Count - 1)
            {
                tickerParts.Add((separator, new Vector4(0.5f, 0.5f, 0.5f, 1f)));
                totalTextWidth += separatorWidth;
            }
        }

        // If all content fits, just draw it centered (no scrolling needed)
        if (totalTextWidth <= width)
        {
            var startX = (width - totalTextWidth) / 2f;
            var cursorPos = ImGui.GetCursorPos();
            ImGui.SetCursorPosX(cursorPos.X + startX);
            
            for (var i = 0; i < tickerParts.Count; i++)
            {
                var (text, color) = tickerParts[i];
                ImGui.TextColored(color, text);
                if (i < tickerParts.Count - 1) ImGui.SameLine(0, 0);
            }
            ImGui.NewLine();
            return;
        }

        // Update ticker animation
        var now = DateTime.Now;
        if (_lastTickerUpdate != DateTime.MinValue)
        {
            var delta = (float)(now - _lastTickerUpdate).TotalSeconds;
            _tickerOffset += delta * TickerSpeed;
            
            // Add separator at the end for seamless loop
            var loopWidth = totalTextWidth + separatorWidth + ImGui.CalcTextSize(separator).X;
            if (_tickerOffset >= loopWidth)
            {
                _tickerOffset -= loopWidth;
            }
        }
        _lastTickerUpdate = now;

        // Draw clipped ticker using DrawList
        var drawList = ImGui.GetWindowDrawList();
        var clipPos = ImGui.GetCursorScreenPos();
        var clipMax = new Vector2(clipPos.X + width, clipPos.Y + height);
        
        drawList.PushClipRect(clipPos, clipMax, true);
        
        // Draw text twice for seamless looping
        var currentX = clipPos.X - _tickerOffset;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            for (var i = 0; i < tickerParts.Count; i++)
            {
                var (text, color) = tickerParts[i];
                var textWidth = ImGui.CalcTextSize(text).X;
                
                // Only draw if visible
                if (currentX + textWidth >= clipPos.X && currentX <= clipMax.X)
                {
                    drawList.AddText(new Vector2(currentX, clipPos.Y), ImGui.GetColorU32(color), text);
                }
                
                currentX += textWidth;
            }
            
            // Add separator between loops
            currentX += separatorWidth;
        }
        
        drawList.PopClipRect();
        
        // Advance cursor
        ImGui.Dummy(new Vector2(width, height));
    }

    private static Vector3[] GetSeriesColors(int count)
    {
        // Predefined distinct colors for up to 8 series
        var colors = new Vector3[]
        {
            new(0.4f, 0.8f, 0.4f),  // Green
            new(0.4f, 0.6f, 1.0f),  // Blue
            new(1.0f, 0.6f, 0.4f),  // Orange
            new(0.9f, 0.4f, 0.9f),  // Magenta
            new(1.0f, 1.0f, 0.4f),  // Yellow
            new(0.4f, 1.0f, 1.0f),  // Cyan
            new(1.0f, 0.4f, 0.4f),  // Red
            new(0.8f, 0.8f, 0.8f),  // Gray
        };

        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
            result[i] = colors[i % colors.Length];
        return result;
    }

    private float[] PrepareDataArray(IReadOnlyList<float> samples)
    {
        if (!_config.ShowEndGap || samples.Count == 0)
        {
            return samples is float[] arrCast ? arrCast : samples.ToArray();
        }

        // Calculate how many empty points to add based on gap percentage
        // The gap creates visual space at the end by adding NaN values that won't be plotted
        var gapPercent = Math.Clamp(_config.EndGapPercent, 0f, 50f);
        var gapCount = Math.Max(1, (int)Math.Ceiling(samples.Count * gapPercent / 100f));

        var result = new float[samples.Count + gapCount];
        for (var i = 0; i < samples.Count; i++)
            result[i] = samples[i];

        // Fill gap with NaN to create actual visual gap
        // ImGui.PlotLines treats NaN as a break in the line
        for (var i = samples.Count; i < result.Length; i++)
            result[i] = float.NaN;

        return result;
    }

    private void DrawValueLabel(float value, Vector2 plotPos, Vector2 plotSize, float min, float max)
    {
        try
        {
            var text = FormatValue(value);
            var textSize = ImGui.CalcTextSize(text);

            // Position the label near the right edge, at the appropriate Y position
            var range = max - min;
            var normalizedY = range > 0 ? (value - min) / range : 0.5f;
            normalizedY = Math.Clamp(normalizedY, 0f, 1f);

            // Calculate screen position for the label with user offsets
            var labelX = plotPos.X + plotSize.X - textSize.X - 8f + _config.ValueLabelOffsetX;
            var labelY = plotPos.Y + plotSize.Y * (1f - normalizedY) - textSize.Y / 2f + _config.ValueLabelOffsetY;

            // Clamp Y to stay within plot bounds (but allow offset to push it outside if desired)
            if (_config.ValueLabelOffsetY == 0f)
            {
                labelY = Math.Clamp(labelY, plotPos.Y, plotPos.Y + plotSize.Y - textSize.Y);
            }

            // Draw using DrawList for precise positioning
            var drawList = ImGui.GetWindowDrawList();
            var bgColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f));
            var textColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

            // Draw background rect
            drawList.AddRectFilled(
                new Vector2(labelX - 2, labelY - 1),
                new Vector2(labelX + textSize.X + 2, labelY + textSize.Y + 1),
                bgColor);

            // Draw text
            drawList.AddText(new Vector2(labelX, labelY), textColor, text);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[SampleGraphWidget] Value label error: {ex.Message}");
        }
    }

    private void DrawTooltip(float[] arr)
    {
        if (!ImGui.IsItemHovered() || arr.Length == 0) return;

        try
        {
            var minRect = ImGui.GetItemRectMin();
            var maxRect = ImGui.GetItemRectMax();
            var mouse = ImGui.GetMousePos();
            var width = maxRect.X - minRect.X;

            if (width <= 0) return;

            var rel = (mouse.X - minRect.X) / width;
            var idx = (int)Math.Floor(rel * arr.Length);
            idx = Math.Clamp(idx, 0, arr.Length - 1);

            var val = arr[idx];
            var valueStr = FormatValue(val);

            // Show percent change from previous value if available
            if (idx > 0)
            {
                var prev = arr[idx - 1];
                if (Math.Abs(prev) < _config.FloatEpsilon)
                {
#if DEBUG
                    ImGui.SetTooltip($"[{idx}] {valueStr} (N/A)");
#else
                    ImGui.SetTooltip($"{valueStr} (N/A)");
#endif
                }
                else
                {
                    var percent = (((double)val - (double)prev) / Math.Abs((double)prev)) * 100.0;
                    var sign = percent < 0 ? "-" : "";
                    var percentAbs = Math.Abs(percent);
                    var percentStr = percentAbs.ToString("0.##", CultureInfo.InvariantCulture);
#if DEBUG
                    ImGui.SetTooltip($"[{idx}] {valueStr} ({sign}{percentStr}%)");
#else
                    ImGui.SetTooltip($"{valueStr} ({sign}{percentStr}%)");
#endif
                }
            }
            else
            {
#if DEBUG
                ImGui.SetTooltip($"[{idx}] {valueStr}");
#else
                ImGui.SetTooltip(valueStr);
#endif
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[SampleGraphWidget] Tooltip error: {ex.Message}");
        }
    }

    private string FormatValue(float v)
    {
        if (Math.Abs(v - Math.Truncate(v)) < _config.FloatEpsilon)
            return ((long)v).ToString("N0", CultureInfo.InvariantCulture);
        return v.ToString("N2", CultureInfo.InvariantCulture);
    }
}
