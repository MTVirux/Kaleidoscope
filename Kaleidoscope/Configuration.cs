using Dalamud.Configuration;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Universalis;

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
/// Time range unit for data filtering.
/// </summary>
public enum TimeRangeUnit
{
    Minutes = 0,
    Hours = 1,
    Days = 2,
    Weeks = 3,
    Months = 4,
    All = 5
}

/// <summary>
/// Time unit for auto-scroll follow mode (includes seconds for finer granularity).
/// </summary>
public enum AutoScrollTimeUnit
{
    Seconds = 0,
    Minutes = 1,
    Hours = 2,
    Days = 3,
    Weeks = 4
}

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
    Bars = 3
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
    /// Whether developer mode stays visible without holding CTRL+ALT.
    /// </summary>
    public bool DeveloperModeEnabled { get; set; } = false;

    public Vector2 MainWindowPos { get; set; } = new(100, 100);
    public Vector2 MainWindowSize { get; set; } = new(600, 400);
    public Vector2 ConfigWindowPos { get; set; } = new(100, 100);
    public Vector2 ConfigWindowSize { get; set; } = new(600, 400);

    public Vector4 MainWindowBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    public Vector4 FullscreenBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);

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
    public TimeRangeUnit GilTrackerTimeRangeUnit { get; set; } = TimeRangeUnit.All;
    public bool GilTrackerShowEndGap { get; set; } = false;
    public float GilTrackerEndGapPercent { get; set; } = 5f;
    public bool GilTrackerShowValueLabel { get; set; } = false;
    public float GilTrackerValueLabelOffsetX { get; set; } = 0f;
    public float GilTrackerValueLabelOffsetY { get; set; } = 0f;
    public float GilTrackerLegendWidth { get; set; } = 120f;
    public bool GilTrackerShowLegend { get; set; } = true;

    // GilTicker settings
    public float GilTickerScrollSpeed { get; set; } = 30f;
    public List<ulong> GilTickerDisabledCharacters { get; set; } = new();

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
    /// Per-data-type display settings (time range, graph bounds, etc.)
    /// </summary>
    public Dictionary<TrackedDataType, DataTrackerSettings> DataTrackerSettings { get; set; } = new();

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
    /// Settings for the Crystal Table tool.
    /// </summary>
    public CrystalTableSettings CrystalTable { get; set; } = new();
    
    /// <summary>
    /// Settings for the Item Table tool.
    /// </summary>
    public ItemTableSettings ItemTable { get; set; } = new();
    
    /// <summary>
    /// Settings for the Item Graph tool.
    /// </summary>
    public ItemGraphSettings ItemGraph { get; set; } = new();
}

/// <summary>
/// Per-data-type tracker settings.
/// </summary>
public class DataTrackerSettings
{
    public bool HideCharacterSelector { get; set; } = false;
    public bool ShowMultipleLines { get; set; } = false;
    public int TimeRangeValue { get; set; } = 7;
    public TimeRangeUnit TimeRangeUnit { get; set; } = TimeRangeUnit.Days;
    public bool ShowXAxisTimestamps { get; set; } = true;
    public bool ShowEndGap { get; set; } = false;
    public float EndGapPercent { get; set; } = 5f;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    public float GraphMinValue { get; set; } = 0f;
    public float GraphMaxValue { get; set; } = 0f; // 0 means use definition default
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public LegendPosition LegendPosition { get; set; } = LegendPosition.Outside;
    public GraphType GraphType { get; set; } = GraphType.Area;
    
    // Auto-scroll settings
    public bool AutoScrollEnabled { get; set; } = false;
    public int AutoScrollTimeValue { get; set; } = 1;
    public AutoScrollTimeUnit AutoScrollTimeUnit { get; set; } = AutoScrollTimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    
    /// <summary>
    /// Calculates the auto-scroll time range in seconds from value and unit.
    /// </summary>
    public double GetAutoScrollTimeRangeSeconds() => AutoScrollTimeUnit.ToSeconds(AutoScrollTimeValue);
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
    public TimeRangeUnit TimeRangeUnit { get; set; } = TimeRangeUnit.Days;
    public bool ShowXAxisTimestamps { get; set; } = true;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    public float LegendWidth { get; set; } = 140f;
    public float LegendHeightPercent { get; set; } = 25f;
    public bool ShowLegend { get; set; } = true;
    public LegendPosition LegendPosition { get; set; } = LegendPosition.Outside;
    public GraphType GraphType { get; set; } = GraphType.Area;
    
    // Auto-scroll settings
    public bool AutoScrollEnabled { get; set; } = false;
    public int AutoScrollTimeValue { get; set; } = 1;
    public AutoScrollTimeUnit AutoScrollTimeUnit { get; set; } = AutoScrollTimeUnit.Hours;
    public float AutoScrollNowPosition { get; set; } = 75f;
    public bool ShowControlsDrawer { get; set; } = true;
    
    /// <summary>
    /// Calculates the auto-scroll time range in seconds from value and unit.
    /// </summary>
    public double GetAutoScrollTimeRangeSeconds() => AutoScrollTimeUnit.ToSeconds(AutoScrollTimeValue);

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
/// Settings for the Crystal Table tool.
/// </summary>
public class CrystalTableSettings
{
    /// <summary>
    /// If true, show columns grouped by element (Fire, Ice, etc.).
    /// </summary>
    public bool GroupByElement { get; set; } = true;

    /// <summary>
    /// If true, show columns grouped by tier (Shard, Crystal, Cluster).
    /// When both GroupByElement and GroupByTier are true, shows detailed element×tier breakdown.
    /// </summary>
    public bool GroupByTier { get; set; } = false;

    /// <summary>
    /// Show a total row at the bottom summing all characters.
    /// </summary>
    public bool ShowTotalRow { get; set; } = true;

    /// <summary>
    /// Colorize element values using their characteristic colors.
    /// </summary>
    public bool ColorizeByElement { get; set; } = true;

    /// <summary>
    /// Allow sorting by clicking column headers.
    /// </summary>
    public bool Sortable { get; set; } = true;

    /// <summary>
    /// In detailed mode, if true sort columns by element first (Fi-Sha, Fi-Cry, Fi-Clu, Ic-Sha...).
    /// If false, sort by tier first (Fi-Sha, Ic-Sha, Wi-Sha..., Fi-Cry, Ic-Cry...).
    /// </summary>
    public bool SortColumnsByElement { get; set; } = true;

    // Element visibility filters
    public bool ShowFire { get; set; } = true;
    public bool ShowIce { get; set; } = true;
    public bool ShowWind { get; set; } = true;
    public bool ShowEarth { get; set; } = true;
    public bool ShowLightning { get; set; } = true;
    public bool ShowWater { get; set; } = true;

    // Tier visibility filters
    public bool ShowShards { get; set; } = true;
    public bool ShowCrystals { get; set; } = true;
    public bool ShowClusters { get; set; } = true;

    /// <summary>
    /// Returns whether the specified element index is visible.
    /// </summary>
    public bool IsElementVisible(int elementIndex) => elementIndex switch
    {
        0 => ShowFire,
        1 => ShowIce,
        2 => ShowWind,
        3 => ShowEarth,
        4 => ShowLightning,
        5 => ShowWater,
        _ => true
    };

    /// <summary>
    /// Returns whether the specified tier index is visible.
    /// </summary>
    public bool IsTierVisible(int tierIndex) => tierIndex switch
    {
        0 => ShowShards,
        1 => ShowCrystals,
        2 => ShowClusters,
        _ => true
    };
}

/// <summary>
/// Settings for the Item Table tool.
/// Implements IItemTableWidgetSettings for automatic widget binding.
/// </summary>
public class ItemTableSettings : Kaleidoscope.Gui.Widgets.IItemTableWidgetSettings
{
    /// <summary>
    /// List of column configurations for items/currencies to display.
    /// </summary>
    public List<Kaleidoscope.Gui.Widgets.ItemColumnConfig> Columns { get; set; } = new();
    
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
    /// Whether to automatically refresh data.
    /// </summary>
    public bool AutoRefresh { get; set; } = true;
    
    /// <summary>
    /// Auto-refresh interval in seconds.
    /// </summary>
    public float RefreshIntervalSeconds { get; set; } = 5f;
    
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
    public Kaleidoscope.Gui.Widgets.TableHorizontalAlignment HorizontalAlignment { get; set; } = 
        Kaleidoscope.Gui.Widgets.TableHorizontalAlignment.Right;
    
    /// <summary>
    /// Vertical alignment for data cell content.
    /// </summary>
    public Kaleidoscope.Gui.Widgets.TableVerticalAlignment VerticalAlignment { get; set; } = 
        Kaleidoscope.Gui.Widgets.TableVerticalAlignment.Top;
    
    /// <summary>
    /// Horizontal alignment for character column content.
    /// </summary>
    public Kaleidoscope.Gui.Widgets.TableHorizontalAlignment CharacterColumnHorizontalAlignment { get; set; } = 
        Kaleidoscope.Gui.Widgets.TableHorizontalAlignment.Left;
    
    /// <summary>
    /// Vertical alignment for character column content.
    /// </summary>
    public Kaleidoscope.Gui.Widgets.TableVerticalAlignment CharacterColumnVerticalAlignment { get; set; } = 
        Kaleidoscope.Gui.Widgets.TableVerticalAlignment.Top;
    
    /// <summary>
    /// Horizontal alignment for header row content.
    /// </summary>
    public Kaleidoscope.Gui.Widgets.TableHorizontalAlignment HeaderHorizontalAlignment { get; set; } = 
        Kaleidoscope.Gui.Widgets.TableHorizontalAlignment.Center;
    
    /// <summary>
    /// Vertical alignment for header row content.
    /// </summary>
    public Kaleidoscope.Gui.Widgets.TableVerticalAlignment HeaderVerticalAlignment { get; set; } = 
        Kaleidoscope.Gui.Widgets.TableVerticalAlignment.Top;
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
    public List<Kaleidoscope.Gui.Widgets.ItemColumnConfig> Series { get; set; } = new();
    
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
    public Kaleidoscope.Gui.Widgets.LegendPosition LegendPosition { get; set; } = Kaleidoscope.Gui.Widgets.LegendPosition.Outside;
    
    /// <summary>The type of graph to render (Area, Line, Stairs, Bars).</summary>
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
    public AutoScrollTimeUnit AutoScrollTimeUnit { get; set; } = AutoScrollTimeUnit.Hours;
    
    /// <summary>Position of "now" on the X-axis when auto-scrolling (0-100%).</summary>
    public float AutoScrollNowPosition { get; set; } = 75f;
    
    /// <summary>Whether to show the controls drawer.</summary>
    public bool ShowControlsDrawer { get; set; } = true;
    
    /// <summary>Numeric value for time range.</summary>
    public int TimeRangeValue { get; set; } = 7;
    
    /// <summary>Unit for time range.</summary>
    public TimeRangeUnit TimeRangeUnit { get; set; } = TimeRangeUnit.Days;
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
}
