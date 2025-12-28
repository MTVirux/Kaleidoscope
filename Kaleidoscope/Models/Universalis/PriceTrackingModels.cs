using Kaleidoscope;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;

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
/// Price match mode for inventory value calculations.
/// Determines which world's sales data to use when calculating item values.
/// </summary>
public enum PriceMatchMode
{
    /// <summary>Use only sales data from the character's specific world.</summary>
    World = 0,
    /// <summary>Use sales data from all worlds in the character's data center.</summary>
    DataCenter = 1,
    /// <summary>Use sales data from all worlds in the character's region.</summary>
    Region = 2,
    /// <summary>Use sales data from the character's region plus Oceania (for low-pop regions).</summary>
    RegionPlusOceania = 3,
    /// <summary>Use sales data from all worlds globally.</summary>
    Global = 4
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

    /// <summary>
    /// Deprecated: Use InventoryValueSettings.ValueScopeMode and related settings instead.
    /// Kept for config backwards compatibility.
    /// </summary>
    [Obsolete("Use InventoryValueSettings.ValueScopeMode instead")]
    public HashSet<int> ExcludedWorldIds { get; set; } = new();

    /// <summary>Item IDs to exclude from tracking.</summary>
    public HashSet<int> ExcludedItemIds { get; set; } = new();

    /// <summary>Whether to automatically fetch initial prices from API for items in inventory.</summary>
    public bool AutoFetchInventoryPrices { get; set; } = true;

    /// <summary>Interval in minutes for cleaning up old price data.</summary>
    public int CleanupIntervalMinutes { get; set; } = 60;

    /// <summary>Interval in hours for refreshing price data from API.</summary>
    public int ApiRefreshIntervalHours { get; set; } = 6;

    // WebSocket Channel Subscriptions
    /// <summary>Whether to subscribe to listings/add events (new listings).</summary>
    public bool SubscribeListingsAdd { get; set; } = true;

    /// <summary>Whether to subscribe to listings/remove events (removed listings).</summary>
    public bool SubscribeListingsRemove { get; set; } = true;

    /// <summary>Whether to subscribe to sales/add events (completed sales).</summary>
    public bool SubscribeSalesAdd { get; set; } = true;

    /// <summary>Whether to filter out sales with large discrepancies from current listings.</summary>
    public bool FilterSalesByListingPrice { get; set; } = true;

    /// <summary>Maximum allowed discrepancy percentage (0-100) between sale price and listing price. Sales outside this range are ignored.</summary>
    public int SaleDiscrepancyThreshold { get; set; } = 50;

    /// <summary>Minimum unit price for sale filtering to apply. Sales below this price skip the discrepancy filter.</summary>
    public int SaleFilterMinimumPrice { get; set; } = 10000;

    /// <summary>Whether to use median instead of average for reference price calculation. More robust against outliers.</summary>
    public bool UseMedianForReference { get; set; } = true;

    /// <summary>Whether to use standard deviation-based filtering instead of fixed percentage threshold.</summary>
    public bool UseStdDevFilter { get; set; } = false;

    /// <summary>Number of standard deviations from mean to consider a price an outlier. Only used when UseStdDevFilter is true.</summary>
    public double StdDevThreshold { get; set; } = 2.0;

    /// <summary>Whether to adjust threshold for bulk/stack sales. Larger quantities get more lenient thresholds.</summary>
    public bool AdjustForBulkSales { get; set; } = true;

    /// <summary>Maximum leniency multiplier for bulk sales (e.g., 1.5 = 50% more lenient for large stacks).</summary>
    public double BulkSaleMaxLeniency { get; set; } = 1.5;
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

    /// <summary>Whether to show latest entries at the top (true) or bottom (false).</summary>
    public bool LatestOnTop { get; set; } = false;

    /// <summary>World filter scope mode (All, ByRegion, ByDataCenter, ByWorld).</summary>
    public PriceTrackingScopeMode FilterScopeMode { get; set; } = PriceTrackingScopeMode.All;

    /// <summary>Selected region names for filtering.</summary>
    public HashSet<string> FilterRegions { get; set; } = new();

    /// <summary>Selected data center names for filtering.</summary>
    public HashSet<string> FilterDataCenters { get; set; } = new();

    /// <summary>Selected world IDs for filtering.</summary>
    public HashSet<int> FilterWorldIds { get; set; } = new();

    /// <summary>Filter by item ID (0 = all).</summary>
    public int FilterItemId { get; set; } = 0;
}

/// <summary>
/// Settings for the Inventory Value tool.
/// Implements IGraphWidgetSettings for automatic graph widget binding.
/// </summary>
public class InventoryValueSettings : IGraphWidgetSettings
{
    // === Tool-specific settings ===
    
    /// <summary>Whether to show multiple lines per character.</summary>
    public bool ShowMultipleLines { get; set; } = true;

    /// <summary>Whether to include retainer inventories.</summary>
    public bool IncludeRetainers { get; set; } = true;

    /// <summary>Whether to include gil in the value calculation.</summary>
    public bool IncludeGil { get; set; } = true;

    // === Hierarchical Price Match Settings ===
    
    /// <summary>
    /// Default price match mode used when no specific override is set.
    /// </summary>
    public PriceMatchMode DefaultPriceMatchMode { get; set; } = PriceMatchMode.Global;

    /// <summary>
    /// Per-region price match mode overrides. Key is region name.
    /// </summary>
    public Dictionary<string, PriceMatchMode> RegionPriceMatchModes { get; set; } = new();

    /// <summary>
    /// Per-data center price match mode overrides. Key is DC name.
    /// </summary>
    public Dictionary<string, PriceMatchMode> DataCenterPriceMatchModes { get; set; } = new();

    /// <summary>
    /// Per-world price match mode overrides. Key is world ID.
    /// </summary>
    public Dictionary<int, PriceMatchMode> WorldPriceMatchModes { get; set; } = new();

    // === Legacy settings (deprecated, kept for config compatibility) ===
    
    /// <summary>Deprecated: Use DefaultPriceMatchMode instead.</summary>
    [Obsolete("Use DefaultPriceMatchMode and hierarchical overrides instead")]
    public PriceTrackingScopeMode ValueScopeMode { get; set; } = PriceTrackingScopeMode.All;

    /// <summary>Deprecated: Use RegionPriceMatchModes instead.</summary>
    [Obsolete("Use RegionPriceMatchModes instead")]
    public HashSet<string> ValueSelectedRegions { get; set; } = new();

    /// <summary>Deprecated: Use DataCenterPriceMatchModes instead.</summary>
    [Obsolete("Use DataCenterPriceMatchModes instead")]
    public HashSet<string> ValueSelectedDataCenters { get; set; } = new();

    /// <summary>Deprecated: Use WorldPriceMatchModes instead.</summary>
    [Obsolete("Use WorldPriceMatchModes instead")]
    public HashSet<int> ValueSelectedWorldIds { get; set; } = new();
    
    // === IGraphWidgetSettings implementation ===
    
    /// <summary>Time range value for the graph.</summary>
    public int TimeRangeValue { get; set; } = 7;

    /// <summary>Time range unit for the graph.</summary>
    public TimeUnit TimeRangeUnit { get; set; } = TimeUnit.Days;

    /// <summary>Whether to show the legend.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Legend width in pixels.</summary>
    public float LegendWidth { get; set; } = 140f;

    /// <summary>Legend position (outside or inside corners).</summary>
    public LegendPosition LegendPosition { get; set; } = LegendPosition.Outside;

    /// <summary>Maximum height of inside legend as percentage of graph height.</summary>
    public float LegendHeightPercent { get; set; } = 25f;

    /// <summary>Graph type for visualization.</summary>
    public GraphType GraphType { get; set; } = GraphType.Area;
    
    /// <summary>Whether to show X-axis timestamps.</summary>
    public bool ShowXAxisTimestamps { get; set; } = true;
    
    /// <summary>Whether to show crosshair on hover.</summary>
    public bool ShowCrosshair { get; set; } = true;
    
    /// <summary>Whether to show horizontal grid lines.</summary>
    public bool ShowGridLines { get; set; } = true;
    
    /// <summary>Whether to show the current value line.</summary>
    public bool ShowCurrentPriceLine { get; set; } = true;
    
    /// <summary>Whether to show a value label at the latest point.</summary>
    public bool ShowValueLabel { get; set; } = true;
    
    /// <summary>X offset for the value label.</summary>
    public float ValueLabelOffsetX { get; set; } = 0f;
    
    /// <summary>Y offset for the value label.</summary>
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    /// <summary>Whether auto-scroll (follow mode) is enabled.</summary>
    public bool AutoScrollEnabled { get; set; } = false;
    
    /// <summary>Auto-scroll time range value.</summary>
    public int AutoScrollTimeValue { get; set; } = 1;
    
    /// <summary>Auto-scroll time range unit.</summary>
    public TimeUnit AutoScrollTimeUnit { get; set; } = TimeUnit.Hours;
    
    /// <summary>Position of "now" on X-axis (0-100%).</summary>
    public float AutoScrollNowPosition { get; set; } = 75f;
    
    /// <summary>Whether to show the controls drawer.</summary>
    public bool ShowControlsDrawer { get; set; } = true;
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

    /// <summary>Item IDs to exclude from the top items list.</summary>
    public HashSet<uint> ExcludedItemIds { get; set; } = new();
}
