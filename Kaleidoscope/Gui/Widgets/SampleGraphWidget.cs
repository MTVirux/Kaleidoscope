using System.Globalization;
using Dalamud.Bindings.ImPlot;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable graph widget for displaying numerical sample data.
/// Renders using ImPlot for advanced graphing capabilities.
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
        /// Whether to auto-scale the Y-axis based on actual data values.
        /// </summary>
        public bool AutoScaleGraph { get; set; } = true;
        
        /// <summary>
        /// Width of the scrollable legend panel in multi-series mode.
        /// </summary>
        public float LegendWidth { get; set; } = 120f;
        
        /// <summary>
        /// Whether to show the legend panel in multi-series mode.
        /// </summary>
        public bool ShowLegend { get; set; } = true;
        
        /// <summary>
        /// The type of graph to render (Area, Line, Stairs, Bars).
        /// </summary>
        public GraphType GraphType { get; set; } = GraphType.Area;
    }

    private readonly GraphConfig _config;
    
    /// <summary>
    /// Set of series names that are currently hidden.
    /// </summary>
    private readonly HashSet<string> _hiddenSeries = new();
    
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
    public void UpdateDisplayOptions(bool showValueLabel, float valueLabelOffsetX = 0f, float valueLabelOffsetY = 0f, bool autoScaleGraph = true, float legendWidth = 120f, bool showLegend = true, GraphType graphType = GraphType.Area)
    {
        _config.ShowValueLabel = showValueLabel;
        _config.ValueLabelOffsetX = valueLabelOffsetX;
        _config.ValueLabelOffsetY = valueLabelOffsetY;
        _config.AutoScaleGraph = autoScaleGraph;
        _config.LegendWidth = legendWidth;
        _config.ShowLegend = showLegend;
        _config.GraphType = graphType;
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
    /// Draws the graph with the provided samples using ImPlot.
    /// </summary>
    /// <param name="samples">The sample data to plot.</param>
    public unsafe void Draw(IReadOnlyList<float> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }

        try
        {
            var avail = ImGui.GetContentRegionAvail();
            var plotSize = new Vector2(Math.Max(1f, avail.X), Math.Max(1f, avail.Y));

            // Calculate Y-axis bounds
            float yMin, yMax;
            if (_config.AutoScaleGraph)
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
                yMin = Math.Max(0f, dataMin - dataRange * 0.1f);
                yMax = dataMax + dataRange * 0.1f;
            }
            else
            {
                yMin = _config.MinValue;
                yMax = _config.MaxValue;
            }

            if (Math.Abs(yMax - yMin) < _config.FloatEpsilon)
            {
                yMax = yMin + 1f;
            }

            // Calculate X-axis range
            var xMax = (double)samples.Count;

            // Configure plot flags - hide legend, title, and mouse position text for clean look
            var plotFlags = ImPlotFlags.NoTitle | ImPlotFlags.NoLegend | ImPlotFlags.NoMenus | ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText;
            
            // Set up axis limits before BeginPlot (use Once to allow user zoom/pan)
            ImPlot.SetNextAxesLimits(0, xMax, yMin, yMax, ImPlotCond.Once);

            if (ImPlot.BeginPlot($"##{_config.PlotId}", plotSize, plotFlags))
            {
                // Configure axes - show Y-axis tick labels for value reference
                ImPlot.SetupAxes("", "", ImPlotAxisFlags.NoTickLabels | ImPlotAxisFlags.NoGridLines, ImPlotAxisFlags.NoGridLines);
                
                // Format Y-axis with thousand separators
                ImPlot.SetupAxisFormat(ImAxis.Y1, YAxisFormatter);
                
                // Constrain axes to prevent negative values
                ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, 0, double.MaxValue);
                ImPlot.SetupAxisLimitsConstraints(ImAxis.Y1, 0, double.MaxValue);

                // Convert samples to arrays for plotting
                var xValues = new double[samples.Count];
                var yValues = new double[samples.Count];
                for (var i = 0; i < samples.Count; i++)
                {
                    xValues[i] = i;
                    yValues[i] = samples[i];
                }

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
                            ImPlot.PlotBars("Gil", xPtr, yPtr, samples.Count, 0.67);
                            break;
                        case GraphType.Area:
                        default:
                            ImPlot.PlotShaded("Gil", xPtr, yPtr, samples.Count);
                            ImPlot.PlotLine("Gil", xPtr, yPtr, samples.Count);
                            break;
                    }
                }

                // Show hover tooltip with series name and value
                if (ImPlot.IsPlotHovered())
                {
                    var mousePos = ImPlot.GetPlotMousePos();
                    var mouseX = mousePos.X;
                    
                    // Find the nearest sample index
                    var nearestIdx = (int)Math.Round(mouseX);
                    if (nearestIdx >= 0 && nearestIdx < samples.Count)
                    {
                        var value = samples[nearestIdx];
                        var tooltipText = $"Gil: {FormatValue(value)}";
                        
                        // Draw annotation at the data point
                        ImPlot.Annotation(nearestIdx, value, 
                            new Vector4(0, 0, 0, 0.85f), new Vector2(10, -10), true, tooltipText);
                    }
                }

                // Draw value label annotation if enabled
                if (_config.ShowValueLabel && samples.Count > 0)
                {
                    var lastValue = samples[^1];
                    var text = FormatValue(lastValue);
                    var pixOffset = new Vector2(_config.ValueLabelOffsetX, _config.ValueLabelOffsetY);
                    ImPlot.Annotation(samples.Count - 1, lastValue, 
                        new Vector4(0, 0, 0, 0.7f), pixOffset, true, text);
                }

                ImPlot.EndPlot();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[SampleGraphWidget] Graph rendering error: {ex.Message}");
            ImGui.TextUnformatted("Error rendering graph");
        }
    }

    /// <summary>
    /// Draws multiple data series overlaid on the same graph with time-aligned data.
    /// All lines extend to current time using their last recorded value.
    /// </summary>
    /// <param name="series">List of data series with names and timestamped values.</param>
    public unsafe void DrawMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series)
    {
        if (series == null || series.Count == 0)
        {
            ImGui.TextUnformatted(_config.NoDataText);
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
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }

        var totalTimeSpan = (globalMaxTime - globalMinTime).TotalSeconds;
        if (totalTimeSpan < 1) totalTimeSpan = 1;

        var xMax = totalTimeSpan;
        var colors = GetSeriesColors(series.Count);

        try
        {
            var avail = ImGui.GetContentRegionAvail();
            
            // Reserve space for scrollable legend on the right (if enabled)
            var legendWidth = _config.ShowLegend ? _config.LegendWidth : 0f;
            var legendPadding = _config.ShowLegend ? 5f : 0f;
            var plotWidth = Math.Max(1f, avail.X - legendWidth - legendPadding);
            var plotSize = new Vector2(plotWidth, Math.Max(1f, avail.Y));

            // Calculate Y-axis bounds based on all visible data
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

            // Configure plot flags - disable legend since we draw custom scrollable one
            var plotFlags = ImPlotFlags.NoTitle | ImPlotFlags.NoMenus | ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText | ImPlotFlags.NoLegend;

            // Set up axis limits (use Once to allow user zoom/pan)
            ImPlot.SetNextAxesLimits(0, xMax, yMin, yMax, ImPlotCond.Once);

            if (ImPlot.BeginPlot($"##{_config.PlotId}_multi", plotSize, plotFlags))
            {
                // Configure axes with minimal chrome - show Y-axis tick labels for value reference
                ImPlot.SetupAxes("", "", ImPlotAxisFlags.NoTickLabels | ImPlotAxisFlags.NoGridLines, ImPlotAxisFlags.NoGridLines);
                
                // Format Y-axis with thousand separators
                ImPlot.SetupAxisFormat(ImAxis.Y1, YAxisFormatter);
                
                // Constrain axes to prevent negative values
                ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, 0, double.MaxValue);
                ImPlot.SetupAxisLimitsConstraints(ImAxis.Y1, 0, double.MaxValue);

                // Draw each series
                for (var seriesIdx = 0; seriesIdx < series.Count; seriesIdx++)
                {
                    var (name, samples) = series[seriesIdx];
                    if (samples == null || samples.Count == 0) continue;

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
                    ImPlot.SetNextFillStyle(new Vector4(color.X, color.Y, color.Z, 0.4f));

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
                                ImPlot.PlotBars(name, xPtr, yPtr, pointCount, barWidth);
                                break;
                            case GraphType.Area:
                            default:
                                ImPlot.PlotShaded(name, xPtr, yPtr, pointCount);
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
                        var tooltipText = $"{nearestSeriesName}: {FormatValue(nearestValue)}";
                        // Draw annotation at the data point
                        ImPlot.Annotation(pointX, pointY, 
                            new Vector4(nearestColor.X, nearestColor.Y, nearestColor.Z, 0.85f), 
                            new Vector2(10, -10), true, tooltipText);
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
                        ImPlot.Annotation(totalTimeSpan, lastValue,
                            new Vector4(color.X, color.Y, color.Z, 0.8f), pixOffset, true, text);
                    }
                }

                ImPlot.EndPlot();
            }
            
            // Draw scrollable legend on the right (if enabled)
            if (_config.ShowLegend)
            {
                ImGui.SameLine();
                DrawScrollableLegend(series, colors, legendWidth, avail.Y);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[SampleGraphWidget] Multi-series rendering error: {ex.Message}");
            ImGui.TextUnformatted("Error rendering graph");
        }
    }
    
    /// <summary>
    /// Draws a scrollable legend for multi-series graphs.
    /// </summary>
    private void DrawScrollableLegend(
        IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series,
        Vector3[] colors,
        float width,
        float height)
    {
        if (ImGui.BeginChild($"##{_config.PlotId}_legend", new Vector2(width, height), true))
        {
            for (var i = 0; i < series.Count; i++)
            {
                var (name, samples) = series[i];
                if (samples == null || samples.Count == 0) continue;
                
                var isHidden = _hiddenSeries.Contains(name);
                var color = colors[i];
                var lastValue = samples[^1].value;
                
                // Use dimmed color for hidden series
                var displayAlpha = isHidden ? 0.35f : 1f;
                
                // Draw colored square indicator
                var drawList = ImGui.GetWindowDrawList();
                var cursorPos = ImGui.GetCursorScreenPos();
                const float indicatorSize = 10f;
                var colorU32 = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, displayAlpha));
                
                if (isHidden)
                {
                    // Draw outline only for hidden series
                    drawList.AddRect(cursorPos, new Vector2(cursorPos.X + indicatorSize, cursorPos.Y + indicatorSize), colorU32);
                }
                else
                {
                    // Draw filled square for visible series
                    drawList.AddRectFilled(cursorPos, new Vector2(cursorPos.X + indicatorSize, cursorPos.Y + indicatorSize), colorU32);
                }
                
                // Make the entire row clickable
                var rowStart = cursorPos;
                
                // Advance cursor past the indicator
                ImGui.Dummy(new Vector2(indicatorSize + 4f, indicatorSize));
                ImGui.SameLine();
                
                // Draw name with dimmed text for hidden series
                if (isHidden)
                {
                    ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
                }
                ImGui.TextUnformatted($"{name}");
                if (isHidden)
                {
                    ImGui.PopStyleColor();
                }
                
                // Make row clickable - use invisible button over the row area
                var rowEnd = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(rowStart);
                if (ImGui.InvisibleButton($"##legend_toggle_{name}", new Vector2(width - 16f, indicatorSize + 2f)))
                {
                    ToggleSeriesVisibility(name);
                }
                
                // Show tooltip on hover
                if (ImGui.IsItemHovered())
                {
                    var statusText = isHidden ? "(hidden)" : "";
                    ImGui.SetTooltip($"{name}: {FormatValue(lastValue)} {statusText}\nClick to toggle");
                }
            }
        }
        ImGui.EndChild();
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

    private string FormatValue(float v)
    {
        if (Math.Abs(v - Math.Truncate(v)) < _config.FloatEpsilon)
            return ((long)v).ToString("N0", CultureInfo.InvariantCulture);
        return v.ToString("N2", CultureInfo.InvariantCulture);
    }
}
