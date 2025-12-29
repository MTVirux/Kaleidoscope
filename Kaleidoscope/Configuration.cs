using Dalamud.Configuration;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using MTGui.Graph;
using MTGui.Table;

namespace Kaleidoscope;

/// <summary>
/// Layout type for windowed vs fullscreen mode.
/// </summary>
public enum LayoutType
{
    Windowed = 0,
    Fullscreen = 1
}

/// <summary>
/// Scope for Universalis market data queries.
/// </summary>
public enum UniversalisScope
{
    /// <summary>Query data for a specific world only.</summary>
    World = 0,
    /// <summary>Query data for the entire data center.</summary>
    DataCenter = 1,
    /// <summary>Query data for the entire region.</summary>
    Region = 2
}

/// <summary>
/// Format for displaying character names.
/// </summary>
public enum CharacterNameFormat
{
    /// <summary>Display full name (e.g., "John Smith").</summary>
    FullName = 0,
    /// <summary>Display first name only (e.g., "John").</summary>
    FirstNameOnly = 1,
    /// <summary>Display last name only (e.g., "Smith").</summary>
    LastNameOnly = 2,
    /// <summary>Display initials (e.g., "J.S.").</summary>
    Initials = 3
}

/// <summary>
/// Sort order for character lists.
/// </summary>
public enum CharacterSortOrder
{
    /// <summary>Sort characters alphabetically by name (A-Z).</summary>
    Alphabetical = 0,
    /// <summary>Sort characters in reverse alphabetical order (Z-A).</summary>
    ReverseAlphabetical = 1,
    /// <summary>Sort characters in the order returned by AutoRetainer.</summary>
    AutoRetainer = 2
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ShowOnStart { get; set; } = true;
    public bool ExclusiveFullscreen { get; set; } = false;
    public bool PinMainWindow { get; set; } = false;
    public bool PinConfigWindow { get; set; } = false;

    /// <summary>
    /// Whether profiling is enabled for draw time tracking.
    /// </summary>
    public bool ProfilerEnabled { get; set; } = false;

    /// <summary>
    /// Whether to log slow operations to the Dalamud log.
    /// </summary>
    public bool ProfilerLogSlowOperations { get; set; } = true;

    /// <summary>
    /// Slow operation threshold in milliseconds. Operations exceeding this will be logged.
    /// </summary>
    public double ProfilerSlowOperationThresholdMs { get; set; } = 5.0;

    /// <summary>
    /// Which stats view to show in the profiler (0=Basic, 1=Percentiles, 2=Rolling, 3=All).
    /// </summary>
    public int ProfilerStatsView { get; set; } = 0;

    /// <summary>
    /// Whether to show the histogram panel in the profiler.
    /// </summary>
    public bool ProfilerShowHistogram { get; set; } = false;

    /// <summary>
    /// Whether to expand child scopes in profiler tool stats.
    /// </summary>
    public bool ProfilerShowChildScopes { get; set; } = true;

    /// <summary>
    /// Whether developer mode stays visible without holding CTRL+ALT.
    /// </summary>
    public bool DeveloperModeEnabled { get; set; } = false;

    /// <summary>
    /// Format for displaying character names throughout the UI.
    /// </summary>
    public CharacterNameFormat CharacterNameFormat { get; set; } = CharacterNameFormat.FullName;

    /// <summary>
    /// Sort order for character lists throughout the UI.
    /// </summary>
    public CharacterSortOrder CharacterSortOrder { get; set; } = CharacterSortOrder.Alphabetical;
    
    /// <summary>
    /// Sort order for item lists in item pickers (alphabetical or by ID).
    /// </summary>
    public Gui.Widgets.ItemSortOrder ItemPickerSortOrder { get; set; } = Gui.Widgets.ItemSortOrder.Alphabetical;

    public Vector2 MainWindowPos { get; set; } = new(100, 100);
    public Vector2 MainWindowSize { get; set; } = new(600, 400);
    public Vector2 ConfigWindowPos { get; set; } = new(100, 100);
    public Vector2 ConfigWindowSize { get; set; } = new(600, 400);

    public Vector4 MainWindowBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    public Vector4 FullscreenBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);

    /// <summary>
    /// Default UI color settings for customization.
    /// </summary>
    public UIColors UIColors { get; set; } = new();

    public float ContentGridCellWidthPercent { get; set; } = 25f;
    public float ContentGridCellHeightPercent { get; set; } = 25f;
    public int GridSubdivisions { get; set; } = 8;
    public bool EditMode { get; set; } = false;

    public List<ContentLayoutState> Layouts { get; set; } = new();
    public string ActiveWindowedLayoutName { get; set; } = string.Empty;
    public string ActiveFullscreenLayoutName { get; set; } = string.Empty;

    /// <summary>
    /// When enabled, layout changes are automatically saved without requiring manual save.
    /// </summary>
    public bool AutoSaveLayoutChanges { get; set; } = false;

    [Obsolete("Use ActiveWindowedLayoutName or ActiveFullscreenLayoutName instead")]
    public string ActiveLayoutName { get; set; } = string.Empty;

    // GilTracker settings
    public bool GilTrackerHideCharacterSelector { get; set; } = false;
    public bool GilTrackerShowMultipleLines { get; set; } = false;
    public int GilTrackerTimeRangeValue { get; set; } = 7;
    public MTTimeUnit GilTrackerTimeRangeUnit { get; set; } = MTTimeUnit.All;
    public bool GilTrackerShowEndGap { get; set; } = false;
    public float GilTrackerEndGapPercent { get; set; } = 5f;
    public bool GilTrackerShowValueLabel { get; set; } = false;
    public float GilTrackerValueLabelOffsetX { get; set; } = 0f;
    public float GilTrackerValueLabelOffsetY { get; set; } = 0f;
    public float GilTrackerLegendWidth { get; set; } = 120f;
    public bool GilTrackerShowLegend { get; set; } = true;

    // Universalis Integration settings
    /// <summary>
    /// The scope for Universalis market data queries (World, DataCenter, or Region).
    /// </summary>
    public UniversalisScope UniversalisQueryScope { get; set; } = UniversalisScope.DataCenter;

    /// <summary>
    /// Override world name for Universalis queries. If empty, uses current character's world.
    /// </summary>
    public string UniversalisWorldOverride { get; set; } = string.Empty;

    /// <summary>
    /// Override data center name for Universalis queries. If empty, uses current character's DC.
    /// </summary>
    public string UniversalisDataCenterOverride { get; set; } = string.Empty;

    /// <summary>
    /// Override region name for Universalis queries. If empty, uses current character's region.
    /// </summary>
    public string UniversalisRegionOverride { get; set; } = string.Empty;

    // Data Tracking settings
    /// <summary>
    /// Set of item IDs that have historical time-series tracking enabled.
    /// When an item is in this set, its quantities are sampled and stored for graphing over time.
    /// This is a global setting - enabling tracking for an item affects all tools.
    /// </summary>
    public HashSet<uint> ItemsWithHistoricalTracking { get; set; } = new();

    /// <summary>
    /// Set of enabled data types for tracking. If null or empty, defaults will be used.
    /// </summary>
    public HashSet<TrackedDataType> EnabledTrackedDataTypes { get; set; } = new()
    {
        TrackedDataType.Gil,
        TrackedDataType.TomestonePoetics,
        TrackedDataType.TomestoneCapped,
        TrackedDataType.OrangeCraftersScrip,
        TrackedDataType.OrangeGatherersScrip,
        TrackedDataType.SackOfNuts,
        TrackedDataType.Ventures
        // Individual crystals are now handled by CrystalTracker tool
    };

    /// <summary>
    /// Custom colors for each tracked data type. Used across all tools for consistent coloring.
    /// Stored as ABGR uint format.
    /// </summary>
    public Dictionary<TrackedDataType, uint> ItemColors { get; set; } = new();

    /// <summary>
    /// Custom colors for game items (keyed by item ID). Used in Item Table and other tools.
    /// Stored as ABGR uint format.
    /// </summary>
    public Dictionary<uint, uint> GameItemColors { get; set; } = new();

    // === Favorites ===
    /// <summary>
    /// Favorite item IDs for quick access in item selectors.
    /// </summary>
    public HashSet<uint> FavoriteItems { get; set; } = new();

    /// <summary>
    /// Favorite currency types (TrackedDataType) for quick access.
    /// </summary>
    public HashSet<TrackedDataType> FavoriteCurrencies { get; set; } = new();

    /// <summary>
    /// Favorite character IDs for quick access in character selectors.
    /// </summary>
    public HashSet<ulong> FavoriteCharacters { get; set; } = new();

    // CrystalTracker settings
    public CrystalTrackerSettings CrystalTracker { get; set; } = new();

    // Price Tracking settings
    /// <summary>
    /// Settings for the Universalis price tracking feature.
    /// </summary>
    public PriceTrackingSettings PriceTracking { get; set; } = new();

    /// <summary>
    /// Settings for the Live Price Feed tool.
    /// </summary>
    public LivePriceFeedSettings LivePriceFeed { get; set; } = new();

    /// <summary>
    /// Settings for the Inventory Value tool.
    /// </summary>
    public InventoryValueSettings InventoryValue { get; set; } = new();

    /// <summary>
    /// Settings for the Top Items tool.
    /// </summary>
    public TopItemsSettings TopItems { get; set; } = new();
    
    /// <summary>
    /// Settings for the Item Table tool.
    /// </summary>
    public ItemTableSettings ItemTable { get; set; } = new();
    
    /// <summary>
    /// Settings for the Item Graph tool.
    /// </summary>
    public ItemGraphSettings ItemGraph { get; set; } = new();

    /// <summary>
    /// User-created tool presets for quick tool configuration.
    /// </summary>
    public List<UserToolPreset> UserToolPresets { get; set; } = new();

    /// <summary>
    /// Settings for the time-series in-memory cache.
    /// </summary>
    public TimeSeriesCacheConfig TimeSeriesCacheConfig { get; set; } = new();

    /// <summary>
    /// Style configuration for all graph widgets.
    /// Customizes colors, spacing, and styling for graph components.
    /// </summary>
    public MTGraphStyleConfig GraphStyle { get; set; } = new();
}

/// <summary>
/// Crystal element types for filtering.
/// </summary>
public enum CrystalElement
{
    Fire = 0,
    Ice = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Water = 5
}

/// <summary>
/// Crystal tier types for filtering.
/// </summary>
public enum CrystalTier
{
    Shard = 0,
    Crystal = 1,
    Cluster = 2
}

/// <summary>
/// How to group crystal data in the display.
/// </summary>
public enum CrystalGrouping
{
    /// <summary>Show total crystals as a single value.</summary>
    None = 0,
    /// <summary>Show separate lines/values per character.</summary>
    ByCharacter = 1,
    /// <summary>Show separate lines/values per element (Fire, Ice, etc.).</summary>
    ByElement = 2,
    /// <summary>Show separate lines/values per element per character.</summary>
    ByCharacterAndElement = 3,
    /// <summary>Show separate lines/values per tier (Shard, Crystal, Cluster).</summary>
    ByTier = 4,
    /// <summary>Show separate lines/values per tier per character.</summary>
    ByCharacterAndTier = 5
}

/// <summary>
/// Settings for the unified Crystal Tracker tool.
/// </summary>
public class CrystalTrackerSettings
{
    // Grouping
    public CrystalGrouping Grouping { get; set; } = CrystalGrouping.ByElement;

    // Tier filters (which tiers to include)
    public bool IncludeShards { get; set; } = true;
    public bool IncludeCrystals { get; set; } = true;
    public bool IncludeClusters { get; set; } = true;

    // Element filters (which elements to include)
    public bool IncludeFire { get; set; } = true;
    public bool IncludeIce { get; set; } = true;
    public bool IncludeWind { get; set; } = true;
    public bool IncludeEarth { get; set; } = true;
    public bool IncludeLightning { get; set; } = true;
    public bool IncludeWater { get; set; } = true;

    // Source filters
    public bool IncludeRetainers { get; set; } = true;

    // Display settings
    public int TimeRangeValue { get; set; } = 7;
    public MTTimeUnit TimeRangeUnit { get; set; } = MTTimeUnit.Days;
    public bool ShowXAxisTimestamps { get; set; } = true;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public MTLegendPosition LegendPosition { get; set; } = MTLegendPosition.Outside;
    public MTGraphType GraphType { get; set; } = MTGraphType.Area;
    
    // Auto-scroll settings
    public bool AutoScrollEnabled { get; set; } = false;
    public int AutoScrollTimeValue { get; set; } = 1;
    public MTTimeUnit AutoScrollTimeUnit { get; set; } = MTTimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    
    /// <summary>
    /// Calculates the auto-scroll time range in seconds from value and unit.
    /// </summary>
    public double GetAutoScrollTimeRangeSeconds() => MTTimeUnitExtensions.ToSeconds(AutoScrollTimeUnit, AutoScrollTimeValue);

    /// <summary>
    /// Gets whether a specific element is included in the filter.
    /// </summary>
    public bool IsElementIncluded(CrystalElement element) => element switch
    {
        CrystalElement.Fire => IncludeFire,
        CrystalElement.Ice => IncludeIce,
        CrystalElement.Wind => IncludeWind,
        CrystalElement.Earth => IncludeEarth,
        CrystalElement.Lightning => IncludeLightning,
        CrystalElement.Water => IncludeWater,
        _ => true
    };

    /// <summary>
    /// Gets whether a specific tier is included in the filter.
    /// </summary>
    public bool IsTierIncluded(CrystalTier tier) => tier switch
    {
        CrystalTier.Shard => IncludeShards,
        CrystalTier.Crystal => IncludeCrystals,
        CrystalTier.Cluster => IncludeClusters,
        _ => true
    };
}

/// <summary>
/// Settings for the Item Table tool.
/// Implements IItemTableWidgetSettings for automatic widget binding.
/// </summary>
public class ItemTableSettings : IItemTableWidgetSettings
{
    /// <summary>
    /// List of column configurations for items/currencies to display.
    /// </summary>
    public List<ItemColumnConfig> Columns { get; set; } = new();
    
    /// <summary>
    /// Whether to show a total row at the bottom summing all characters.
    /// </summary>
    public bool ShowTotalRow { get; set; } = true;
    
    /// <summary>
    /// Whether to allow sorting by clicking column headers.
    /// </summary>
    public bool Sortable { get; set; } = true;
    
    /// <summary>
    /// Whether to include retainer inventory in item counts.
    /// </summary>
    public bool IncludeRetainers { get; set; } = true;
    
    /// <summary>
    /// Width of the character name column.
    /// </summary>
    public float CharacterColumnWidth { get; set; } = 120f;
    
    /// <summary>
    /// Optional custom color for the character name column.
    /// </summary>
    public System.Numerics.Vector4? CharacterColumnColor { get; set; }
    
    /// <summary>
    /// Index of the column to sort by (0 = character name, 1+ = data columns).
    /// </summary>
    public int SortColumnIndex { get; set; } = 0;
    
    /// <summary>
    /// Whether to sort in ascending order.
    /// </summary>
    public bool SortAscending { get; set; } = true;
    
    /// <summary>
    /// Whether to show the action buttons row (Add Item, Add Currency, Refresh).
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;
    
    /// <summary>
    /// Whether to use compact number notation (e.g., 10M instead of 10,000,000).
    /// </summary>
    public bool UseCompactNumbers { get; set; } = false;
    
    /// <summary>
    /// Optional custom color for the table header row.
    /// </summary>
    public System.Numerics.Vector4? HeaderColor { get; set; }
    
    /// <summary>
    /// Optional custom color for even-numbered rows (0, 2, 4...).
    /// </summary>
    public System.Numerics.Vector4? EvenRowColor { get; set; }
    
    /// <summary>
    /// Optional custom color for odd-numbered rows (1, 3, 5...).
    /// </summary>
    public System.Numerics.Vector4? OddRowColor { get; set; }
    
    /// <summary>
    /// Whether to use the full character name width as the minimum column width.
    /// When enabled, the character column will be at least as wide as the longest name.
    /// </summary>
    public bool UseFullNameWidth { get; set; } = true;
    
    /// <summary>
    /// Whether to auto-size all data columns to equal widths.
    /// The character column width (based on name width if UseFullNameWidth) takes priority.
    /// </summary>
    public bool AutoSizeEqualColumns { get; set; } = false;
    
    /// <summary>
    /// Horizontal alignment for data cell content.
    /// </summary>
    public MTTableHorizontalAlignment HorizontalAlignment { get; set; } = 
        MTTableHorizontalAlignment.Right;
    
    /// <summary>
    /// Vertical alignment for data cell content.
    /// </summary>
    public MTTableVerticalAlignment VerticalAlignment { get; set; } = 
        MTTableVerticalAlignment.Top;
    
    /// <summary>
    /// Horizontal alignment for character column content.
    /// </summary>
    public MTTableHorizontalAlignment CharacterColumnHorizontalAlignment { get; set; } = 
        MTTableHorizontalAlignment.Left;
    
    /// <summary>
    /// Vertical alignment for character column content.
    /// </summary>
    public MTTableVerticalAlignment CharacterColumnVerticalAlignment { get; set; } = 
        MTTableVerticalAlignment.Top;
    
    /// <summary>
    /// Horizontal alignment for header row content.
    /// </summary>
    public MTTableHorizontalAlignment HeaderHorizontalAlignment { get; set; } = 
        MTTableHorizontalAlignment.Center;
    
    /// <summary>
    /// Vertical alignment for header row content.
    /// </summary>
    public MTTableVerticalAlignment HeaderVerticalAlignment { get; set; } = 
        MTTableVerticalAlignment.Top;
    
    /// <summary>
    /// Set of character IDs that are hidden from the table.
    /// </summary>
    public HashSet<ulong> HiddenCharacters { get; set; } = new();
    
    /// <summary>
    /// Whether to use multi-select character filtering (show only selected characters).
    /// When false, shows all characters (with HiddenCharacters for individual hiding).
    /// </summary>
    public bool UseCharacterFilter { get; set; } = false;
    
    /// <summary>
    /// List of selected character IDs when UseCharacterFilter is enabled.
    /// Empty list means "All Characters".
    /// </summary>
    public List<ulong> SelectedCharacterIds { get; set; } = new();
    
    /// <summary>
    /// Grouping mode for table rows (Character, World, DataCenter, Region, All).
    /// </summary>
    public TableGroupingMode GroupingMode { get; set; } = 
        TableGroupingMode.Character;
    
    /// <summary>
    /// Whether to hide the character/group column when GroupingMode is All.
    /// </summary>
    public bool HideCharacterColumnInAllMode { get; set; } = false;
    
    /// <summary>
    /// List of merged column groups. Each group combines multiple columns into one.
    /// </summary>
    public List<MergedColumnGroup> MergedColumnGroups { get; set; } = new();
    
    /// <summary>
    /// List of merged row groups. Each group combines multiple character rows into one.
    /// </summary>
    public List<MergedRowGroup> MergedRowGroups { get; set; } = new();
    
    /// <summary>
    /// Mode for determining cell text colors.
    /// </summary>
    public TableTextColorMode TextColorMode { get; set; } = 
        TableTextColorMode.DontUse;
    
    /// <summary>
    /// Whether to show expandable retainer breakdown for characters with retainer data.
    /// When enabled, characters with retainers can be expanded to show per-retainer counts.
    /// </summary>
    public bool ShowRetainerBreakdown { get; set; } = true;
    
    /// <summary>
    /// Whether to hide rows where all column values are zero.
    /// </summary>
    public bool HideZeroRows { get; set; } = false;
    
    /// <summary>
    /// Settings for special grouping filters (unlocked when specific item combinations are selected).
    /// </summary>
    public Kaleidoscope.Models.SpecialGroupingSettings SpecialGrouping { get; set; } = new();
}

/// <summary>
/// Settings for the Item Graph tool.
/// Implements IGraphWidgetSettings for automatic graph widget binding.
/// </summary>
public class ItemGraphSettings : Kaleidoscope.Models.IGraphWidgetSettings
{
    /// <summary>
    /// List of series configurations for items/currencies to display as graph lines.
    /// </summary>
    public List<ItemColumnConfig> Series { get; set; } = new();
    
    /// <summary>
    /// Whether to include retainer inventory in item counts.
    /// </summary>
    public bool IncludeRetainers { get; set; } = true;
    
    /// <summary>
    /// Whether to show separate lines for each character instead of aggregating.
    /// </summary>
    public bool ShowPerCharacter { get; set; } = false;
    
    /// <summary>
    /// Whether to show the action buttons row (Add Item, Add Currency, Refresh).
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;
    
    /// <summary>
    /// Whether to use compact number notation (e.g., 10M instead of 10,000,000).
    /// </summary>
    public bool UseCompactNumbers { get; set; } = false;
    
    // === IGraphWidgetSettings implementation ===
    
    /// <summary>Width of the scrollable legend panel on the right side of the graph.</summary>
    public float LegendWidth { get; set; } = 140f;
    
    /// <summary>Maximum height of the inside legend as a percentage of the graph height.</summary>
    public float LegendHeightPercent { get; set; } = 25f;
    
    /// <summary>Whether to show the legend panel.</summary>
    public bool ShowLegend { get; set; } = true;
    
    /// <summary>Position of the legend (inside or outside the graph).</summary>
    public MTLegendPosition LegendPosition { get; set; } = MTLegendPosition.Outside;
    
    /// <summary>The type of graph to render (Area, Line, Stairs, Bars).</summary>
    public MTGraphType GraphType { get; set; } = MTGraphType.Area;
    
    /// <summary>Whether to show X-axis timestamps.</summary>
    public bool ShowXAxisTimestamps { get; set; } = true;
    
    /// <summary>Whether to show crosshair on hover.</summary>
    public bool ShowCrosshair { get; set; } = true;
    
    /// <summary>Whether to show horizontal grid lines.</summary>
    public bool ShowGridLines { get; set; } = true;
    
    /// <summary>Whether to show the current value line.</summary>
    public bool ShowCurrentPriceLine { get; set; } = true;
    
    /// <summary>Whether to show a value label at the latest point.</summary>
    public bool ShowValueLabel { get; set; } = false;
    
    /// <summary>X offset for the value label.</summary>
    public float ValueLabelOffsetX { get; set; } = 0f;
    
    /// <summary>Y offset for the value label.</summary>
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    /// <summary>Whether auto-scroll is enabled.</summary>
    public bool AutoScrollEnabled { get; set; } = false;
    
    /// <summary>Numeric value for auto-scroll time range.</summary>
    public int AutoScrollTimeValue { get; set; } = 1;
    
    /// <summary>Unit for auto-scroll time range.</summary>
    public MTTimeUnit AutoScrollTimeUnit { get; set; } = MTTimeUnit.Hours;
    
    /// <summary>Position of "now" on the X-axis when auto-scrolling (0-100%).</summary>
    public float AutoScrollNowPosition { get; set; } = 75f;
    
    /// <summary>Whether to show the controls drawer.</summary>
    public bool ShowControlsDrawer { get; set; } = true;
    
    /// <summary>Numeric value for time range.</summary>
    public int TimeRangeValue { get; set; } = 7;
    
    /// <summary>Unit for time range.</summary>
    public MTTimeUnit TimeRangeUnit { get; set; } = MTTimeUnit.Days;
    
    // === Character filtering settings (aligned with ItemTableSettings) ===
    
    /// <summary>
    /// Whether to use multi-select character filtering (show only selected characters).
    /// When false, shows all characters.
    /// </summary>
    public bool UseCharacterFilter { get; set; } = false;
    
    /// <summary>
    /// List of selected character IDs when UseCharacterFilter is enabled.
    /// Empty list means "All Characters".
    /// </summary>
    public List<ulong> SelectedCharacterIds { get; set; } = new();
    
    /// <summary>
    /// Grouping mode for graph series (Character, World, DataCenter, Region, All).
    /// Maps to the same modes as ItemTableSettings for consistency.
    /// </summary>
    public TableGroupingMode GroupingMode { get; set; } = 
        TableGroupingMode.Character;
    
    /// <summary>
    /// Special grouping settings (AllCrystals element/tier filtering, AllGil merging).
    /// Aligned with ItemTableSettings for feature parity.
    /// </summary>
    public Kaleidoscope.Models.SpecialGroupingSettings SpecialGrouping { get; set; } = new();
}

/// <summary>
/// View mode for the unified Data Tool.
/// </summary>
public enum DataToolViewMode
{
    /// <summary>Display data as a table with characters as rows and items as columns.</summary>
    Table = 0,
    /// <summary>Display data as a time-series graph.</summary>
    Graph = 1
}

/// <summary>
/// Unified settings for the Data Tool, combining table and graph functionality.
/// Implements both IItemTableWidgetSettings and IGraphWidgetSettings for widget binding.
/// </summary>
public class DataToolSettings : 
    IItemTableWidgetSettings,
    Kaleidoscope.Models.IGraphWidgetSettings
{
    // === View Mode ===
    
    /// <summary>
    /// Current view mode (Table or Graph).
    /// </summary>
    public DataToolViewMode ViewMode { get; set; } = DataToolViewMode.Table;
    
    // === Shared Settings (used by both views) ===
    
    /// <summary>
    /// List of column/series configurations for items/currencies to display.
    /// Used as columns in table view and series in graph view.
    /// </summary>
    public List<ItemColumnConfig> Columns { get; set; } = new();
    
    /// <summary>
    /// Whether to include retainer inventory in item counts.
    /// </summary>
    public bool IncludeRetainers { get; set; } = true;
    
    /// <summary>
    /// Whether to show the action buttons row (Add Item, Add Currency, Refresh, View Toggle).
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;
    
    /// <summary>
    /// Whether to use compact number notation (e.g., 10M instead of 10,000,000).
    /// </summary>
    public bool UseCompactNumbers { get; set; } = false;
    
    /// <summary>
    /// Whether to use multi-select character filtering (show only selected characters).
    /// </summary>
    public bool UseCharacterFilter { get; set; } = false;
    
    /// <summary>
    /// List of selected character IDs when UseCharacterFilter is enabled.
    /// </summary>
    public List<ulong> SelectedCharacterIds { get; set; } = new();
    
    /// <summary>
    /// Grouping mode (Character, World, DataCenter, Region, All).
    /// </summary>
    public TableGroupingMode GroupingMode { get; set; } = 
        TableGroupingMode.Character;
    
    /// <summary>
    /// Special grouping settings (AllCrystals element/tier filtering, AllGil merging).
    /// </summary>
    public Kaleidoscope.Models.SpecialGroupingSettings SpecialGrouping { get; set; } = new();
    
    // === Table-Specific Settings ===
    
    /// <summary>
    /// Whether to show a total row at the bottom summing all characters.
    /// </summary>
    public bool ShowTotalRow { get; set; } = true;
    
    /// <summary>
    /// Whether to allow sorting by clicking column headers.
    /// </summary>
    public bool Sortable { get; set; } = true;
    
    /// <summary>
    /// Width of the character name column.
    /// </summary>
    public float CharacterColumnWidth { get; set; } = 120f;
    
    /// <summary>
    /// Optional custom color for the character name column.
    /// </summary>
    public System.Numerics.Vector4? CharacterColumnColor { get; set; }
    
    /// <summary>
    /// Index of the column to sort by (0 = character name, 1+ = data columns).
    /// </summary>
    public int SortColumnIndex { get; set; } = 0;
    
    /// <summary>
    /// Whether to sort in ascending order.
    /// </summary>
    public bool SortAscending { get; set; } = true;
    
    /// <summary>
    /// Optional custom color for the table header row.
    /// </summary>
    public System.Numerics.Vector4? HeaderColor { get; set; }
    
    /// <summary>
    /// Optional custom color for even-numbered rows.
    /// </summary>
    public System.Numerics.Vector4? EvenRowColor { get; set; }
    
    /// <summary>
    /// Optional custom color for odd-numbered rows.
    /// </summary>
    public System.Numerics.Vector4? OddRowColor { get; set; }
    
    /// <summary>
    /// Whether to use the full character name width as the minimum column width.
    /// </summary>
    public bool UseFullNameWidth { get; set; } = true;
    
    /// <summary>
    /// Whether to auto-size all data columns to equal widths.
    /// </summary>
    public bool AutoSizeEqualColumns { get; set; } = false;
    
    /// <summary>
    /// Horizontal alignment for data cell content.
    /// </summary>
    public MTTableHorizontalAlignment HorizontalAlignment { get; set; } = 
        MTTableHorizontalAlignment.Right;
    
    /// <summary>
    /// Vertical alignment for data cell content.
    /// </summary>
    public MTTableVerticalAlignment VerticalAlignment { get; set; } = 
        MTTableVerticalAlignment.Top;
    
    /// <summary>
    /// Horizontal alignment for character column content.
    /// </summary>
    public MTTableHorizontalAlignment CharacterColumnHorizontalAlignment { get; set; } = 
        MTTableHorizontalAlignment.Left;
    
    /// <summary>
    /// Vertical alignment for character column content.
    /// </summary>
    public MTTableVerticalAlignment CharacterColumnVerticalAlignment { get; set; } = 
        MTTableVerticalAlignment.Top;
    
    /// <summary>
    /// Horizontal alignment for header row content.
    /// </summary>
    public MTTableHorizontalAlignment HeaderHorizontalAlignment { get; set; } = 
        MTTableHorizontalAlignment.Center;
    
    /// <summary>
    /// Vertical alignment for header row content.
    /// </summary>
    public MTTableVerticalAlignment HeaderVerticalAlignment { get; set; } = 
        MTTableVerticalAlignment.Top;
    
    /// <summary>
    /// Set of character IDs that are hidden from the table.
    /// </summary>
    public HashSet<ulong> HiddenCharacters { get; set; } = new();
    
    /// <summary>
    /// Whether to hide the character/group column when GroupingMode is All.
    /// </summary>
    public bool HideCharacterColumnInAllMode { get; set; } = false;
    
    /// <summary>
    /// List of merged column groups.
    /// </summary>
    public List<MergedColumnGroup> MergedColumnGroups { get; set; } = new();
    
    /// <summary>
    /// List of merged row groups.
    /// </summary>
    public List<MergedRowGroup> MergedRowGroups { get; set; } = new();
    
    /// <summary>
    /// Mode for determining cell text colors.
    /// </summary>
    public TableTextColorMode TextColorMode { get; set; } = 
        TableTextColorMode.PreferredItemColors;
    
    /// <summary>
    /// Whether to show expandable retainer breakdown for characters with retainer data in table view.
    /// When enabled, characters with retainers can be expanded to show per-retainer counts.
    /// </summary>
    public bool ShowRetainerBreakdown { get; set; } = true;
    
    /// <summary>
    /// Whether to show separate lines for each retainer in graph view.
    /// When enabled, each retainer's inventory is shown as a separate series.
    /// </summary>
    public bool ShowRetainerBreakdownInGraph { get; set; } = false;
    
    /// <summary>
    /// Whether to hide rows where all column values are zero.
    /// </summary>
    public bool HideZeroRows { get; set; } = false;
    
    // === Graph-Specific Settings (IGraphWidgetSettings implementation) ===
    
    /// <summary>Width of the scrollable legend panel.</summary>
    public float LegendWidth { get; set; } = 140f;
    
    /// <summary>Maximum height of the inside legend as a percentage of the graph height.</summary>
    public float LegendHeightPercent { get; set; } = 25f;
    
    /// <summary>Whether to show the legend panel.</summary>
    public bool ShowLegend { get; set; } = true;
    
    /// <summary>Position of the legend (inside or outside the graph).</summary>
    public MTLegendPosition LegendPosition { get; set; } = 
        MTLegendPosition.InsideTopLeft;
    
    /// <summary>The type of graph to render (Area, Line, Stairs, Bars).</summary>
    public MTGraphType GraphType { get; set; } = MTGraphType.Area;
    
    /// <summary>Whether to show X-axis timestamps.</summary>
    public bool ShowXAxisTimestamps { get; set; } = true;
    
    /// <summary>Whether to show crosshair on hover.</summary>
    public bool ShowCrosshair { get; set; } = true;
    
    /// <summary>Whether to show horizontal grid lines.</summary>
    public bool ShowGridLines { get; set; } = true;
    
    /// <summary>Whether to show the current value line.</summary>
    public bool ShowCurrentPriceLine { get; set; } = true;
    
    /// <summary>Whether to show a value label at the latest point.</summary>
    public bool ShowValueLabel { get; set; } = false;
    
    /// <summary>X offset for the value label.</summary>
    public float ValueLabelOffsetX { get; set; } = 0f;
    
    /// <summary>Y offset for the value label.</summary>
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    /// <summary>Whether auto-scroll is enabled.</summary>
    public bool AutoScrollEnabled { get; set; } = false;
    
    /// <summary>Numeric value for auto-scroll time range.</summary>
    public int AutoScrollTimeValue { get; set; } = 1;
    
    /// <summary>Unit for auto-scroll time range.</summary>
    public MTTimeUnit AutoScrollTimeUnit { get; set; } = MTTimeUnit.Hours;
    
    /// <summary>Position of "now" on the X-axis when auto-scrolling (0-100%).</summary>
    public float AutoScrollNowPosition { get; set; } = 75f;
    
    /// <summary>Whether to show the controls drawer.</summary>
    public bool ShowControlsDrawer { get; set; } = true;
    
    /// <summary>Numeric value for time range.</summary>
    public int TimeRangeValue { get; set; } = 7;
    
    /// <summary>Unit for time range.</summary>
    public MTTimeUnit TimeRangeUnit { get; set; } = MTTimeUnit.Days;
}

public class ContentLayoutState
{
    public string Name { get; set; } = string.Empty;
    public LayoutType Type { get; set; } = LayoutType.Windowed;
    public List<ContentComponentState> Components { get; set; } = new();
    public List<ToolLayoutState> Tools { get; set; } = new();

    public bool AutoAdjustResolution { get; set; } = true;
    public int Columns { get; set; } = 16;
    public int Rows { get; set; } = 9;
    public int Subdivisions { get; set; } = 8;
    public int GridResolutionMultiplier { get; set; } = 2;
}

public class ContentComponentState
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
}

/// <summary>
/// Persisted state for a tool within a layout.
/// </summary>
public class ToolLayoutState
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CustomTitle { get; set; } = null;
    public Vector2 Position { get; set; } = new(50, 50);
    public Vector2 Size { get; set; } = new(300, 200);
    public bool Visible { get; set; } = true;

    public bool BackgroundEnabled { get; set; } = false;
    public bool HeaderVisible { get; set; } = true;
    public bool OutlineEnabled { get; set; } = true;
    public Vector4 BackgroundColor { get; set; } = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);

    public float GridCol { get; set; } = 0f;
    public float GridRow { get; set; } = 0f;
    public float GridColSpan { get; set; } = 4f;
    public float GridRowSpan { get; set; } = 4f;
    public bool HasGridCoords { get; set; } = false;
    
    /// <summary>
    /// List of series names that are hidden in this tool instance.
    /// </summary>
    public List<string> HiddenSeries { get; set; } = new();
    
    /// <summary>
    /// Tool-specific settings stored as key-value pairs.
    /// Each tool type can store its own settings here for instance-specific persistence.
    /// </summary>
    public Dictionary<string, object?> ToolSettings { get; set; } = new();
}

/// <summary>
/// User-created tool preset for saving and loading tool configurations.
/// </summary>
public class UserToolPreset
{
    /// <summary>
    /// Unique identifier for this preset.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// User-defined name for this preset.
    /// </summary>
    public string Name { get; set; } = "New Preset";
    
    /// <summary>
    /// The tool type ID (e.g., "DataTable", "DataGraph") this preset applies to.
    /// </summary>
    public string ToolType { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional description of what this preset contains.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Date/time when this preset was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Date/time when this preset was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// The serialized tool settings. Uses the same format as ToolLayoutState.ToolSettings.
    /// </summary>
    public Dictionary<string, object?> Settings { get; set; } = new();
}

/// <summary>
/// Default UI color settings for customization.
/// These colors serve as defaults that can be overridden at the tool/widget level.
/// </summary>
public class UIColors
{
    // Window backgrounds
    /// <summary>Default background color for the main window.</summary>
    public Vector4 MainWindowBackground { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    
    /// <summary>Default background color for fullscreen mode.</summary>
    public Vector4 FullscreenBackground { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    
    // Tool defaults
    /// <summary>Default background color for new tools.</summary>
    public Vector4 ToolBackground { get; set; } = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
    
    /// <summary>Default header text color for tools.</summary>
    public Vector4 ToolHeaderText { get; set; } = new(1f, 1f, 1f, 1f);
    
    /// <summary>Default border/outline color for tools in edit mode.</summary>
    public Vector4 ToolBorder { get; set; } = new(0.43f, 0.43f, 0.5f, 0.5f);
    
    // Table colors
    /// <summary>Default color for table header rows.</summary>
    public Vector4 TableHeader { get; set; } = new(0.26f, 0.26f, 0.28f, 1f);
    
    /// <summary>Default color for even table rows.</summary>
    public Vector4 TableRowEven { get; set; } = new(0f, 0f, 0f, 0f);
    
    /// <summary>Default color for odd table rows.</summary>
    public Vector4 TableRowOdd { get; set; } = new(0.1f, 0.1f, 0.1f, 0.3f);
    
    /// <summary>Default color for table total rows.</summary>
    public Vector4 TableTotalRow { get; set; } = new(0.3f, 0.3f, 0.3f, 0.5f);
    
    // Text colors
    /// <summary>Default primary text color.</summary>
    public Vector4 TextPrimary { get; set; } = new(1f, 1f, 1f, 1f);
    
    /// <summary>Default secondary/muted text color.</summary>
    public Vector4 TextSecondary { get; set; } = new(0.7f, 0.7f, 0.7f, 1f);
    
    /// <summary>Default disabled text color.</summary>
    public Vector4 TextDisabled { get; set; } = new(0.5f, 0.5f, 0.5f, 1f);
    
    // Accent colors
    /// <summary>Primary accent color for highlights and selections.</summary>
    public Vector4 AccentPrimary { get; set; } = new(0.26f, 0.59f, 0.98f, 1f);
    
    /// <summary>Success/positive color (e.g., for price increases).</summary>
    public Vector4 AccentSuccess { get; set; } = new(0.2f, 0.8f, 0.2f, 1f);
    
    /// <summary>Warning color (e.g., for alerts).</summary>
    public Vector4 AccentWarning { get; set; } = new(1f, 0.7f, 0.3f, 1f);
    
    /// <summary>Error/negative color (e.g., for price decreases).</summary>
    public Vector4 AccentError { get; set; } = new(0.9f, 0.2f, 0.2f, 1f);
    
    // Quick access bar colors
    /// <summary>Background color for the quick access bar.</summary>
    public Vector4 QuickAccessBarBackground { get; set; } = new(0.1f, 0.1f, 0.1f, 0.87f);
    
    /// <summary>Separator color in the quick access bar.</summary>
    public Vector4 QuickAccessBarSeparator { get; set; } = new(0.31f, 0.31f, 0.31f, 1f);
    
    // Graph colors
    /// <summary>Default graph line/fill color when no specific color is assigned.</summary>
    public Vector4 GraphDefault { get; set; } = new(0.4f, 0.6f, 0.9f, 1f);
    
    /// <summary>Graph axis and grid line color.</summary>
    public Vector4 GraphAxis { get; set; } = new(0.5f, 0.5f, 0.5f, 0.5f);
    
    /// <summary>
    /// Resets all colors to their default values.
    /// </summary>
    public void ResetToDefaults()
    {
        MainWindowBackground = new(0.06f, 0.06f, 0.06f, 0.94f);
        FullscreenBackground = new(0.06f, 0.06f, 0.06f, 0.94f);
        ToolBackground = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
        ToolHeaderText = new(1f, 1f, 1f, 1f);
        ToolBorder = new(0.43f, 0.43f, 0.5f, 0.5f);
        TableHeader = new(0.26f, 0.26f, 0.28f, 1f);
        TableRowEven = new(0f, 0f, 0f, 0f);
        TableRowOdd = new(0.1f, 0.1f, 0.1f, 0.3f);
        TableTotalRow = new(0.3f, 0.3f, 0.3f, 0.5f);
        TextPrimary = new(1f, 1f, 1f, 1f);
        TextSecondary = new(0.7f, 0.7f, 0.7f, 1f);
        TextDisabled = new(0.5f, 0.5f, 0.5f, 1f);
        AccentPrimary = new(0.26f, 0.59f, 0.98f, 1f);
        AccentSuccess = new(0.2f, 0.8f, 0.2f, 1f);
        AccentWarning = new(1f, 0.7f, 0.3f, 1f);
        AccentError = new(0.9f, 0.2f, 0.2f, 1f);
        QuickAccessBarBackground = new(0.1f, 0.1f, 0.1f, 0.87f);
        QuickAccessBarSeparator = new(0.31f, 0.31f, 0.31f, 1f);
        GraphDefault = new(0.4f, 0.6f, 0.9f, 1f);
        GraphAxis = new(0.5f, 0.5f, 0.5f, 0.5f);
    }
}
