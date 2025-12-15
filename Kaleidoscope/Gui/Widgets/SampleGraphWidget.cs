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
    }

    private readonly GraphConfig _config;

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
    public void UpdateDisplayOptions(bool showLatestValue, bool showEndGap, float endGapPercent, bool showValueLabel)
    {
        _config.ShowLatestValue = showLatestValue;
        _config.ShowEndGap = showEndGap;
        _config.EndGapPercent = endGapPercent;
        _config.ShowValueLabel = showValueLabel;
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
    /// Draws multiple data series overlaid on the same graph.
    /// Uses ImGui DrawList to render multiple lines on a single plot area.
    /// </summary>
    /// <param name="series">List of data series with names and values.</param>
    public void DrawMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<float> samples)> series)
    {
        if (series == null || series.Count == 0)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }

        var min = _config.MinValue;
        var max = _config.MaxValue;

        if (Math.Abs(max - min) < _config.FloatEpsilon)
        {
            max = min + 1f;
        }

        try
        {
            var avail = ImGui.GetContentRegionAvail();
            var graphWidth = Math.Max(1f, avail.X);
            var graphHeight = Math.Max(1f, avail.Y - 20f); // Reserve space for legend

            // Draw legend at top
            var legendColors = GetSeriesColors(series.Count);
            for (var i = 0; i < series.Count; i++)
            {
                var (name, samples) = series[i];
                var color = legendColors[i];
                var latestVal = samples != null && samples.Count > 0 ? FormatValue(samples[^1]) : "N/A";
                
                if (i > 0) ImGui.SameLine();
                ImGui.TextColored(new Vector4(color.X, color.Y, color.Z, 1f), $"{name}: {latestVal}");
                ImGui.SameLine();
                ImGui.TextUnformatted(" ");
            }
            ImGui.NewLine();

            // Get plot area position
            var plotPos = ImGui.GetCursorScreenPos();
            var plotSize = new Vector2(graphWidth, graphHeight);

            // Draw background rect for the plot area
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), 
                ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.5f)));

            // Find the maximum sample count for X-axis scaling
            var maxSampleCount = 1;
            foreach (var (_, samples) in series)
            {
                if (samples != null && samples.Count > maxSampleCount)
                    maxSampleCount = samples.Count;
            }

            // Apply end gap to max sample count
            if (_config.ShowEndGap)
            {
                var gapPercent = Math.Clamp(_config.EndGapPercent, 0f, 50f);
                maxSampleCount = (int)(maxSampleCount * (1f + gapPercent / 100f));
            }

            // Draw each series as a polyline
            for (var seriesIdx = 0; seriesIdx < series.Count; seriesIdx++)
            {
                var (name, samples) = series[seriesIdx];
                if (samples == null || samples.Count < 2) continue;

                var color = legendColors[seriesIdx];
                var colorU32 = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 1f));

                for (var i = 0; i < samples.Count - 1; i++)
                {
                    var x1 = plotPos.X + (i / (float)maxSampleCount) * plotSize.X;
                    var x2 = plotPos.X + ((i + 1) / (float)maxSampleCount) * plotSize.X;
                    
                    var y1Normalized = (samples[i] - min) / (max - min);
                    var y2Normalized = (samples[i + 1] - min) / (max - min);
                    
                    var y1 = plotPos.Y + plotSize.Y - (y1Normalized * plotSize.Y);
                    var y2 = plotPos.Y + plotSize.Y - (y2Normalized * plotSize.Y);

                    drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), colorU32, 1.5f);
                }
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

            // Calculate screen position for the label
            var labelX = plotPos.X + plotSize.X - textSize.X - 8f;
            var labelY = plotPos.Y + plotSize.Y * (1f - normalizedY) - textSize.Y / 2f;

            // Clamp Y to stay within plot bounds
            labelY = Math.Clamp(labelY, plotPos.Y, plotPos.Y + plotSize.Y - textSize.Y);

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
