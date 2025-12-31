using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using MTGui.Common;
using MTGui.Graph;
using MTGui.Table;

namespace Kaleidoscope;

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
    /// Number format configuration for displaying values.
    /// </summary>
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    /// <summary>
    /// Whether to use compact number notation (e.g., 10M instead of 10,000,000).
    /// </summary>
    [Obsolete("Use NumberFormat instead. Kept for migration.")]
    public bool UseCompactNumbers
    {
        get => NumberFormat.Style == NumberFormatStyle.Compact;
        set => NumberFormat.Style = value ? NumberFormatStyle.Compact : NumberFormatStyle.Standard;
    }
    
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
    /// Number format configuration for displaying values.
    /// </summary>
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    /// <summary>
    /// Whether to use compact number notation (e.g., 10M instead of 10,000,000).
    /// </summary>
    [Obsolete("Use NumberFormat instead. Kept for migration.")]
    public bool UseCompactNumbers
    {
        get => NumberFormat.Style == NumberFormatStyle.Compact;
        set => NumberFormat.Style = value ? NumberFormatStyle.Compact : NumberFormatStyle.Standard;
    }
    
    // === IGraphWidgetSettings implementation ===
    
    /// <summary>Mode for determining series colors in the graph.</summary>
    public Models.GraphColorMode ColorMode { get; set; } = Models.GraphColorMode.PreferredItemColors;
    
    /// <summary>Width of the scrollable legend panel on the right side of the graph.</summary>
    public float LegendWidth { get; set; } = 140f;
    
    /// <summary>Maximum height of the inside legend as a percentage of the graph height.</summary>
    public float LegendHeightPercent { get; set; } = 25f;
    
    /// <summary>Whether to show the legend panel.</summary>
    public bool ShowLegend { get; set; } = true;
    
    /// <summary>Whether the legend is collapsed.</summary>
    public bool LegendCollapsed { get; set; } = false;
    
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
    /// Number format configuration for the table view.
    /// </summary>
    public NumberFormatConfig TableNumberFormat { get; set; } = new();
    
    /// <summary>
    /// Number format configuration for the graph view.
    /// </summary>
    public NumberFormatConfig GraphNumberFormat { get; set; } = new();
    
    // Explicit interface implementations for NumberFormat
    NumberFormatConfig IItemTableWidgetSettings.NumberFormat
    {
        get => TableNumberFormat;
        set => TableNumberFormat = value;
    }
    
    NumberFormatConfig MTGui.Graph.IMTGraphSettings.NumberFormat
    {
        get => GraphNumberFormat;
        set => GraphNumberFormat = value;
    }
    
    /// <summary>
    /// Whether to use compact number notation (e.g., 10M instead of 10,000,000).
    /// </summary>
    [Obsolete("Use TableNumberFormat/GraphNumberFormat instead. Kept for migration.")]
    public bool UseCompactNumbers
    {
        get => TableNumberFormat.Style == NumberFormatStyle.Compact;
        set => TableNumberFormat.Style = value ? NumberFormatStyle.Compact : NumberFormatStyle.Standard;
    }
    
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
    
    /// <summary>Mode for determining series colors in the graph.</summary>
    public Models.GraphColorMode ColorMode { get; set; } = Models.GraphColorMode.PreferredItemColors;
    
    /// <summary>Width of the scrollable legend panel.</summary>
    public float LegendWidth { get; set; } = 140f;
    
    /// <summary>Maximum height of the inside legend as a percentage of the graph height.</summary>
    public float LegendHeightPercent { get; set; } = 25f;
    
    /// <summary>Whether to show the legend panel.</summary>
    public bool ShowLegend { get; set; } = true;
    
    /// <summary>Whether the legend is collapsed.</summary>
    public bool LegendCollapsed { get; set; } = false;
    
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
