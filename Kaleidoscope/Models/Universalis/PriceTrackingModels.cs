using Kaleidoscope;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Gui.Widgets.Common;
using Kaleidoscope.Gui.Widgets.Graph;

namespace Kaleidoscope.Models.Universalis;

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
/// Settings for the Websocket Feed tool.
/// </summary>
public sealed class WebsocketFeedSettings
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
/// <remarks>
/// The graph-display properties below are intentionally duplicated in the sibling settings classes
/// ItemGraphSettings, DataToolSettings, GraphWidgetSettings and ItemSalesTrackingSettings. Each class
/// is serialized independently into the user's config JSON, so the flat property layout must be
/// preserved; do NOT collapse these into a shared nested object, as that would change persisted JSON
/// paths and break loading of existing configs.
/// </remarks>
public sealed class InventoryValueSettings : IGraphWidgetSettings
{
    /// <summary>Whether to show multiple lines per character.</summary>
    public bool ShowMultipleLines { get; set; } = true;

    /// <summary>Whether to include retainer inventories.</summary>
    public bool IncludeRetainers { get; set; } = true;

    /// <summary>Whether to include gil in the value calculation.</summary>
    public bool IncludeGil { get; set; } = true;

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
    
    /// <summary>Mode for determining series colors in the graph.</summary>
    public GraphColorMode ColorMode { get; set; } = GraphColorMode.PreferredCharacterColors;
    
    /// <summary>Time range value for the graph.</summary>
    public int TimeRangeValue { get; set; } = 7;

    /// <summary>Time range unit for the graph.</summary>
    public TimeUnit TimeRangeUnit { get; set; } = TimeUnit.Days;

    /// <summary>Whether to show the legend.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Whether the legend is collapsed.</summary>
    public bool LegendCollapsed { get; set; } = false;

    /// <summary>Legend position (inside corners).</summary>
    public LegendPosition LegendPosition { get; set; } = LegendPosition.InsideTopLeft;

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
    
    /// <summary>Number format configuration for displayed values.</summary>
    public NumberFormatConfig NumberFormat { get; set; } = new();
}

/// <summary>
/// Settings for the Top Inventory Value Items tool.
/// </summary>
public sealed class TopInventoryValueItemsSettings
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
