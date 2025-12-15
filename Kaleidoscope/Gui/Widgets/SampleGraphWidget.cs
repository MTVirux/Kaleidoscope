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
            var graphWidth = avail.X <= 0f ? 0f : avail.X;
            var graphHeight = avail.Y <= 0f ? 0f : avail.Y;

            ImGui.BeginChild($"{_config.PlotId}_child", new Vector2(graphWidth, graphHeight), false);

            var childAvailAfterBegin = ImGui.GetContentRegionAvail();
            ImGui.SetNextItemWidth(Math.Max(1f, childAvailAfterBegin.X));

            var plotSize = new Vector2(
                Math.Max(1f, childAvailAfterBegin.X),
                Math.Max(1f, childAvailAfterBegin.Y));

            ImGui.PlotLines($"##{_config.PlotId}", arr, arr.Length, "", min, max, plotSize);

            // Draw value label if enabled
            if (_config.ShowValueLabel && samples.Count > 0)
            {
                DrawValueLabel(samples[^1], plotSize, min, max);
            }

            ImGui.EndChild();

            // Show tooltip with value when hovering
            DrawTooltip(samples is float[] arrCast ? arrCast : samples.ToArray());

            // Draw latest value at line end if enabled
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
    /// Draws multiple data series on the same graph.
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
            var heightPerSeries = (avail.Y - (series.Count - 1) * 2) / series.Count; // 2px spacing between series

            for (var i = 0; i < series.Count; i++)
            {
                var (name, samples) = series[i];
                if (samples == null || samples.Count == 0)
                {
                    ImGui.TextUnformatted($"{name}: No data");
                    continue;
                }

                var arr = PrepareDataArray(samples);
                var plotSize = new Vector2(Math.Max(1f, avail.X), Math.Max(1f, heightPerSeries));

                // Draw series label
                var latestVal = samples[^1];
                var labelText = $"{name}: {FormatValue(latestVal)}";
                ImGui.TextUnformatted(labelText);

                ImGui.PlotLines($"##{_config.PlotId}_{i}", arr, arr.Length, "", min, max, plotSize);

                if (i < series.Count - 1)
                    ImGui.Spacing();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[SampleGraphWidget] Multi-series rendering error: {ex.Message}");
            ImGui.TextUnformatted("Error rendering graph");
        }
    }

    private float[] PrepareDataArray(IReadOnlyList<float> samples)
    {
        if (!_config.ShowEndGap || samples.Count == 0)
        {
            return samples is float[] arrCast ? arrCast : samples.ToArray();
        }

        // Calculate how many empty points to add based on gap percentage
        var gapPercent = Math.Clamp(_config.EndGapPercent, 0f, 50f);
        var gapCount = Math.Max(1, (int)Math.Ceiling(samples.Count * gapPercent / 100f));

        var result = new float[samples.Count + gapCount];
        for (var i = 0; i < samples.Count; i++)
            result[i] = samples[i];

        // Fill gap with the last value to maintain visual continuity
        var lastVal = samples[^1];
        for (var i = samples.Count; i < result.Length; i++)
            result[i] = lastVal;

        return result;
    }

    private void DrawValueLabel(float value, Vector2 plotSize, float min, float max)
    {
        try
        {
            var text = FormatValue(value);
            var textSize = ImGui.CalcTextSize(text);

            // Position the label near the right edge, at the appropriate Y position
            var range = max - min;
            var normalizedY = range > 0 ? (value - min) / range : 0.5f;
            var yPos = plotSize.Y * (1 - normalizedY); // Invert because Y grows downward

            var cursorPos = ImGui.GetCursorPos();
            var labelX = plotSize.X - textSize.X - 5;
            var labelY = Math.Clamp(yPos - textSize.Y / 2, 0, plotSize.Y - textSize.Y);

            ImGui.SetCursorPos(new Vector2(cursorPos.X + labelX, cursorPos.Y + labelY));
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(cursorPos);
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
                    ImGui.SetTooltip($"{valueStr} (N/A)");
                }
                else
                {
                    var percent = (((double)val - (double)prev) / Math.Abs((double)prev)) * 100.0;
                    var sign = percent < 0 ? "-" : "";
                    var percentAbs = Math.Abs(percent);
                    var percentStr = percentAbs.ToString("0.##", CultureInfo.InvariantCulture);
                    ImGui.SetTooltip($"{valueStr} ({sign}{percentStr}%)");
                }
            }
            else
            {
                ImGui.SetTooltip(valueStr);
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
