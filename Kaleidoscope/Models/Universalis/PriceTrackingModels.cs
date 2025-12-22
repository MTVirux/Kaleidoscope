using Kaleidoscope;

namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Retention policy type for price data.
/// </summary>
public enum PriceRetentionType
{
    /// <summary>Keep data for a specified number of days.</summary>
    ByTime = 0,
    /// <summary>Keep data up to a specified size in MB.</summary>
    BySize = 1
}

/// <summary>
/// Scope mode for price tracking subscriptions.
/// </summary>
public enum PriceTrackingScopeMode
{
    /// <summary>Track all worlds/DCs automatically.</summary>
    All = 0,
    /// <summary>Track specific regions.</summary>
    ByRegion = 1,
    /// <summary>Track specific data centers.</summary>
    ByDataCenter = 2,
    /// <summary>Track specific worlds.</summary>
    ByWorld = 3
}

/// <summary>
/// Configuration for price tracking feature.
/// </summary>
public class PriceTrackingSettings
{
    /// <summary>Whether price tracking is enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Retention policy type (by time or by size).</summary>
    public PriceRetentionType RetentionType { get; set; } = PriceRetentionType.ByTime;

    /// <summary>Number of days to retain price data when using ByTime retention.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Maximum size in MB for price data when using BySize retention.</summary>
    public int RetentionSizeMb { get; set; } = 100;

    /// <summary>Scope mode for which worlds/DCs to track.</summary>
    public PriceTrackingScopeMode ScopeMode { get; set; } = PriceTrackingScopeMode.All;

    /// <summary>Selected region names when using ByRegion scope mode.</summary>
    public HashSet<string> SelectedRegions { get; set; } = new();

    /// <summary>Selected data center names when using ByDataCenter scope mode.</summary>
    public HashSet<string> SelectedDataCenters { get; set; } = new();

    /// <summary>Selected world IDs when using ByWorld scope mode.</summary>
    public HashSet<int> SelectedWorldIds { get; set; } = new();

    /// <summary>Item IDs to exclude from tracking.</summary>
    public HashSet<int> ExcludedItemIds { get; set; } = new();

    /// <summary>Whether to automatically fetch initial prices from API for items in inventory.</summary>
    public bool AutoFetchInventoryPrices { get; set; } = true;

    /// <summary>Interval in minutes for cleaning up old price data.</summary>
    public int CleanupIntervalMinutes { get; set; } = 60;

    /// <summary>Interval in hours for refreshing price data from API.</summary>
    public int ApiRefreshIntervalHours { get; set; } = 6;
}

/// <summary>
/// Settings for the Live Price Feed tool.
/// </summary>
public class LivePriceFeedSettings
{
    /// <summary>Maximum number of entries to display.</summary>
    public int MaxEntries { get; set; } = 100;

    /// <summary>Whether to show listing add events.</summary>
    public bool ShowListingsAdd { get; set; } = true;

    /// <summary>Whether to show listing remove events.</summary>
    public bool ShowListingsRemove { get; set; } = true;

    /// <summary>Whether to show sale events.</summary>
    public bool ShowSales { get; set; } = true;

    /// <summary>Whether to auto-scroll to latest entry.</summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>Filter by world ID (0 = all).</summary>
    public int FilterWorldId { get; set; } = 0;

    /// <summary>Filter by item ID (0 = all).</summary>
    public int FilterItemId { get; set; } = 0;
}

/// <summary>
/// Settings for the Inventory Value tool.
/// </summary>
public class InventoryValueSettings
{
    /// <summary>Time range value for the graph.</summary>
    public int TimeRangeValue { get; set; } = 7;

    /// <summary>Time range unit for the graph.</summary>
    public TimeRangeUnit TimeRangeUnit { get; set; } = TimeRangeUnit.Days;

    /// <summary>Whether to show multiple lines per character.</summary>
    public bool ShowMultipleLines { get; set; } = true;

    /// <summary>Whether to include retainer inventories.</summary>
    public bool IncludeRetainers { get; set; } = true;

    /// <summary>Whether to include gil in the value calculation.</summary>
    public bool IncludeGil { get; set; } = true;

    /// <summary>Whether to show the legend.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Legend width in pixels.</summary>
    public float LegendWidth { get; set; } = 140f;

    /// <summary>Graph type for visualization.</summary>
    public GraphType GraphType { get; set; } = GraphType.Area;

    /// <summary>Whether to auto-scale the graph.</summary>
    public bool AutoScaleGraph { get; set; } = true;
}

/// <summary>
/// Settings for the Top Items tool.
/// </summary>
public class TopItemsSettings
{
    /// <summary>Maximum number of items to display.</summary>
    public int MaxItems { get; set; } = 100;

    /// <summary>Whether to show all characters combined or per-character.</summary>
    public bool ShowAllCharacters { get; set; } = true;

    /// <summary>Selected character ID when not showing all (0 = current).</summary>
    public ulong SelectedCharacterId { get; set; } = 0;

    /// <summary>Whether to include retainer inventories.</summary>
    public bool IncludeRetainers { get; set; } = true;

    /// <summary>Whether to include gil in the list.</summary>
    public bool IncludeGil { get; set; } = true;

    /// <summary>Minimum value threshold to show an item.</summary>
    public long MinValueThreshold { get; set; } = 0;

    /// <summary>Whether to group by item (combining quantities) or show individual stacks.</summary>
    public bool GroupByItem { get; set; } = true;
}
