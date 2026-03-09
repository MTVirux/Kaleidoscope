namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Represents a group of series that can be toggled together in the legend.
/// A series can belong to multiple groups.
/// </summary>
public sealed class GraphSeriesGroup
{
    /// <summary>
    /// Display name for this group (shown in legend).
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Color for this group's indicator in the legend (RGB).
    /// </summary>
    public Vector3 Color { get; init; } = new(0.6f, 0.6f, 0.6f);
    
    /// <summary>
    /// Names of all series that belong to this group.
    /// </summary>
    public required IReadOnlyList<string> SeriesNames { get; init; }
}
