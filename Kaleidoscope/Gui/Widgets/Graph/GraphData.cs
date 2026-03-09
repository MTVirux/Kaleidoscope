namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Represents a single data series for graph rendering.
/// Abstracts over both index-based and time-based data sources.
/// </summary>
public sealed class GraphSeriesData
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
    
    /// <summary>
    /// Names of groups this series belongs to. A series can belong to multiple groups.
    /// If any group is hidden, the series will be hidden.
    /// </summary>
    public IReadOnlyList<string>? GroupNames { get; init; }
}

/// <summary>
/// Prepared data for graph rendering, including computed bounds and all series.
/// </summary>
public sealed class PreparedGraphData
{
    /// <summary>
    /// All series to render.
    /// </summary>
    public required IReadOnlyList<GraphSeriesData> Series { get; init; }
    
    /// <summary>
    /// Groups for the legend. Each group can toggle visibility of its member series.
    /// </summary>
    public IReadOnlyList<GraphSeriesGroup>? Groups { get; init; }
    
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
    /// Cached count of visible series. Updated by <see cref="UpdateVisibleSeriesCount"/>.
    /// </summary>
    private int _visibleSeriesCount;
    
    /// <summary>
    /// Whether this graph has multiple visible series (affects rendering decisions like bar width).
    /// </summary>
    public bool HasMultipleSeries => _visibleSeriesCount > 1;
    
    /// <summary>
    /// Gets the number of currently visible series.
    /// </summary>
    public int VisibleSeriesCount => _visibleSeriesCount;
    
    /// <summary>
    /// Whether this graph has multiple series total (including hidden), affects legend display.
    /// This ensures the legend remains visible when series are hidden, allowing users to re-enable them.
    /// </summary>
    public bool HasMultipleSeriesTotal => Series.Count > 1;
    
    /// <summary>
    /// Recomputes the cached visible series count. Call this after creating or modifying series visibility.
    /// </summary>
    public void UpdateVisibleSeriesCount()
    {
        var count = 0;
        foreach (var s in Series)
        {
            if (s.Visible) count++;
        }
        _visibleSeriesCount = count;
    }
    
    /// <summary>
    /// Whether this graph has any groups defined.
    /// </summary>
    public bool HasGroups => Groups is { Count: > 0 };
}
