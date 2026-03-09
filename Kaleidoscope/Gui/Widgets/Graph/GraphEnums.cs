namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Graph visualization type for time-series data.
/// </summary>
public enum GraphType
{
    /// <summary>Filled area chart - good for showing volume over time.</summary>
    Area = 0,
    
    /// <summary>Simple line chart.</summary>
    Line = 1,
    
    /// <summary>Step/stairs chart - shows discrete value changes.</summary>
    Stairs = 2,
    
    /// <summary>Vertical bar chart.</summary>
    Bars = 3,
    
    /// <summary>Step/stairs chart with filled area - combines stairs with shaded region.</summary>
    StairsArea = 4
}

/// <summary>
/// Unified time unit for data filtering, auto-scroll, and time range selection.
/// </summary>
public enum TimeUnit
{
    /// <summary>Seconds - for fine-grained auto-scroll control.</summary>
    Seconds = 0,
    
    /// <summary>Minutes.</summary>
    Minutes = 1,
    
    /// <summary>Hours.</summary>
    Hours = 2,
    
    /// <summary>Days.</summary>
    Days = 3,
    
    /// <summary>Weeks.</summary>
    Weeks = 4,
    
    /// <summary>Months - approximate (30 days).</summary>
    Months = 5,
    
    /// <summary>All time - no time limit applied.</summary>
    All = 6
}

/// <summary>
/// Specifies where the legend should be positioned in the graph.
/// </summary>
public enum LegendPosition
{
    /// <summary>Legend is drawn outside the graph area (to the right).</summary>
    Outside = 0,
    
    /// <summary>Legend is drawn inside the graph area (top-left corner).</summary>
    InsideTopLeft = 1,
    
    /// <summary>Legend is drawn inside the graph area (top-right corner).</summary>
    InsideTopRight = 2,
    
    /// <summary>Legend is drawn inside the graph area (bottom-left corner).</summary>
    InsideBottomLeft = 3,
    
    /// <summary>Legend is drawn inside the graph area (bottom-right corner).</summary>
    InsideBottomRight = 4
}

/// <summary>
/// Extension methods for TimeUnit enum.
/// </summary>
public static class TimeUnitExtensions
{
    /// <summary>
    /// Converts a time value and unit to total seconds.
    /// </summary>
    /// <param name="unit">The time unit.</param>
    /// <param name="value">The numeric value.</param>
    /// <returns>The total time in seconds.</returns>
    public static double ToSeconds(this TimeUnit unit, int value) => unit switch
    {
        TimeUnit.Seconds => value,
        TimeUnit.Minutes => value * 60,
        TimeUnit.Hours => value * 3600,
        TimeUnit.Days => value * 86400,
        TimeUnit.Weeks => value * 604800,
        TimeUnit.Months => value * 2592000, // 30 days
        TimeUnit.All => double.MaxValue,
        _ => 3600
    };
    
    /// <summary>
    /// Gets the display name for a time unit.
    /// </summary>
    public static string GetDisplayName(this TimeUnit unit) => unit switch
    {
        TimeUnit.Seconds => "Seconds",
        TimeUnit.Minutes => "Minutes",
        TimeUnit.Hours => "Hours",
        TimeUnit.Days => "Days",
        TimeUnit.Weeks => "Weeks",
        TimeUnit.Months => "Months",
        TimeUnit.All => "All Time",
        _ => "Unknown"
    };
    
    /// <summary>
    /// Gets the short name for a time unit (for compact UI).
    /// </summary>
    public static string GetShortName(this TimeUnit unit) => unit switch
    {
        TimeUnit.Seconds => "sec",
        TimeUnit.Minutes => "min",
        TimeUnit.Hours => "hr",
        TimeUnit.Days => "day",
        TimeUnit.Weeks => "wk",
        TimeUnit.Months => "mo",
        TimeUnit.All => "all",
        _ => "?"
    };
}

/// <summary>
/// Extension methods for GraphType enum.
/// </summary>
public static class MTGraphTypeExtensions
{
    /// <summary>
    /// Gets the display name for a graph type.
    /// </summary>
    public static string GetDisplayName(this GraphType type) => type switch
    {
        GraphType.Area => "Area",
        GraphType.Line => "Line",
        GraphType.Stairs => "Stairs",
        GraphType.Bars => "Bars",
        GraphType.StairsArea => "Stairs Area",
        _ => "Unknown"
    };
    
    /// <summary>
    /// Gets a description of the graph type.
    /// </summary>
    public static string GetDescription(this GraphType type) => type switch
    {
        GraphType.Area => "Filled area chart - good for showing volume over time",
        GraphType.Line => "Simple line chart",
        GraphType.Stairs => "Step chart showing discrete value changes",
        GraphType.Bars => "Vertical bar chart",
        GraphType.StairsArea => "Step chart with filled area below",
        _ => "Unknown graph type"
    };
}
