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

        var arr = samples is float[] arrCast ? arrCast : samples.ToArray();
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
            ImGui.EndChild();

            // Show tooltip with value when hovering
            DrawTooltip(arr);
        }
        catch (Exception ex)
        {
            // Fall back to default small plot if anything goes wrong
            LogService.Debug($"[SampleGraphWidget] Graph rendering error: {ex.Message}");
            ImGui.PlotLines($"##{_config.PlotId}", arr, arr.Length);
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
            var currentStr = $"{idx}:{FormatValue(val)}";

            // Show percent change from previous value if available
            if (idx > 0)
            {
                var prev = arr[idx - 1];
                if (Math.Abs(prev) < _config.FloatEpsilon)
                {
                    ImGui.SetTooltip($"{currentStr} (N/A)");
                }
                else
                {
                    var percent = (((double)val - (double)prev) / Math.Abs((double)prev)) * 100.0;
                    var sign = percent < 0 ? "-" : "";
                    var percentAbs = Math.Abs(percent);
                    var percentStr = percentAbs.ToString("0.##", CultureInfo.InvariantCulture);
                    ImGui.SetTooltip($"{currentStr} ({sign}{percentStr}%)");
                }
            }
            else
            {
                ImGui.SetTooltip(currentStr);
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
