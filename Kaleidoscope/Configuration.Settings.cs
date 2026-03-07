using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Gui.Widgets.Common;
using Kaleidoscope.Gui.Widgets.Graph;
using Kaleidoscope.Gui.Widgets.Table;

namespace Kaleidoscope;

/// <summary>
/// Settings for the Item Table tool.
/// Implements IItemTableWidgetSettings for automatic widget binding.
/// </summary>
public sealed class ItemTableSettings : IItemTableWidgetSettings
{
    public List<ItemColumnConfig> Columns { get; set; } = new();
    
    public bool ShowTotalRow { get; set; } = true;
    
    public bool Sortable { get; set; } = true;
    
    public bool IncludeRetainers { get; set; } = true;
    
    public float CharacterColumnWidth { get; set; } = 120f;
    
    public System.Numerics.Vector4? CharacterColumnColor { get; set; }
    
    /// <summary>
    /// Index of the column to sort by (0 = character name, 1+ = data columns).
    /// </summary>
    public int SortColumnIndex { get; set; } = 0;
    
    public bool SortAscending { get; set; } = true;
    
    /// <summary>
    /// Whether to show the action buttons row (Add Item, Add Currency, Refresh).
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;
    
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    public System.Numerics.Vector4? HeaderColor { get; set; }
    
    public System.Numerics.Vector4? EvenRowColor { get; set; }
    
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
    
    public TableHorizontalAlignment HorizontalAlignment { get; set; } = 
        TableHorizontalAlignment.Right;
    
    public TableVerticalAlignment VerticalAlignment { get; set; } = 
        TableVerticalAlignment.Top;
    
    public TableHorizontalAlignment CharacterColumnHorizontalAlignment { get; set; } = 
        TableHorizontalAlignment.Left;
    
    public TableVerticalAlignment CharacterColumnVerticalAlignment { get; set; } = 
        TableVerticalAlignment.Top;
    
    public TableHorizontalAlignment HeaderHorizontalAlignment { get; set; } = 
        TableHorizontalAlignment.Center;
    
    public TableVerticalAlignment HeaderVerticalAlignment { get; set; } = 
        TableVerticalAlignment.Top;
    
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
    
    public TableGroupingMode GroupingMode { get; set; } = 
        TableGroupingMode.Character;
    
    public bool HideCharacterColumnInAllMode { get; set; } = false;
    
    public List<MergedColumnGroup> MergedColumnGroups { get; set; } = new();
    
    public List<MergedRowGroup> MergedRowGroups { get; set; } = new();
    
    public TableTextColorMode TextColorMode { get; set; } = 
        TableTextColorMode.DontUse;
    
    /// <summary>
    /// Whether to show expandable retainer breakdown for characters with retainer data.
    /// When enabled, characters with retainers can be expanded to show per-retainer counts.
    /// </summary>
    public bool ShowRetainerBreakdown { get; set; } = true;
    
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
public sealed class ItemGraphSettings : Kaleidoscope.Models.IGraphWidgetSettings
{
    public List<ItemColumnConfig> Series { get; set; } = new();
    
    public bool IncludeRetainers { get; set; } = true;
    
    /// <summary>
    /// Whether to show the action buttons row (Add Item, Add Currency, Refresh).
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;
    
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    public Models.GraphColorMode ColorMode { get; set; } = Models.GraphColorMode.PreferredItemColors;
    
    /// <summary>Width of the scrollable legend panel on the right side of the graph.</summary>
    public float LegendWidth { get; set; } = 140f;
    
    /// <summary>Maximum height of the inside legend as a percentage of the graph height.</summary>
    public float LegendHeightPercent { get; set; } = 25f;
    
    public bool ShowLegend { get; set; } = true;
    
    public bool LegendCollapsed { get; set; } = false;
    
    public LegendPosition LegendPosition { get; set; } = LegendPosition.Outside;
    
    public GraphType GraphType { get; set; } = GraphType.Area;
    
    public bool ShowXAxisTimestamps { get; set; } = true;
    
    public bool ShowCrosshair { get; set; } = true;
    
    public bool ShowGridLines { get; set; } = true;
    
    public bool ShowCurrentPriceLine { get; set; } = true;
    
    /// <summary>Whether to show a value label at the latest point.</summary>
    public bool ShowValueLabel { get; set; } = false;
    
    public float ValueLabelOffsetX { get; set; } = 0f;
    
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    public bool AutoScrollEnabled { get; set; } = false;
    
    public int AutoScrollTimeValue { get; set; } = 1;
    
    public TimeUnit AutoScrollTimeUnit { get; set; } = TimeUnit.Hours;
    
    /// <summary>Position of "now" on the X-axis when auto-scrolling (0-100%).</summary>
    public float AutoScrollNowPosition { get; set; } = 75f;
    
    public bool ShowControlsDrawer { get; set; } = true;
    
    public int TimeRangeValue { get; set; } = 7;
    
    public TimeUnit TimeRangeUnit { get; set; } = TimeUnit.Days;
    
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
public sealed class DataToolSettings : 
    IItemTableWidgetSettings,
    Kaleidoscope.Models.IGraphWidgetSettings
{
    public DataToolViewMode ViewMode { get; set; } = DataToolViewMode.Table;
    
    /// <summary>
    /// List of column/series configurations for items/currencies to display.
    /// Used as columns in table view and series in graph view.
    /// </summary>
    public List<ItemColumnConfig> Columns { get; set; } = new();
    
    public bool IncludeRetainers { get; set; } = true;
    
    /// <summary>
    /// Whether to show the action buttons row (Add Item, Add Currency, Refresh, View Toggle).
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;
    
    public NumberFormatConfig TableNumberFormat { get; set; } = new();
    
    public NumberFormatConfig GraphNumberFormat { get; set; } = new();
    
    NumberFormatConfig IItemTableWidgetSettings.NumberFormat
    {
        get => TableNumberFormat;
        set => TableNumberFormat = value;
    }
    
    NumberFormatConfig Kaleidoscope.Gui.Widgets.Graph.IGraphSettings.NumberFormat
    {
        get => GraphNumberFormat;
        set => GraphNumberFormat = value;
    }
    
    /// <summary>
    /// Whether to use multi-select character filtering (show only selected characters).
    /// </summary>
    public bool UseCharacterFilter { get; set; } = false;
    
    public List<ulong> SelectedCharacterIds { get; set; } = new();
    
    public TableGroupingMode GroupingMode { get; set; } = 
        TableGroupingMode.Character;
    
    /// <summary>
    /// Special grouping settings (AllCrystals element/tier filtering, AllGil merging).
    /// </summary>
    public Kaleidoscope.Models.SpecialGroupingSettings SpecialGrouping { get; set; } = new();
    
    public bool ShowTotalRow { get; set; } = true;
    
    public bool Sortable { get; set; } = true;
    
    public float CharacterColumnWidth { get; set; } = 120f;
    
    public System.Numerics.Vector4? CharacterColumnColor { get; set; }
    
    /// <summary>
    /// Index of the column to sort by (0 = character name, 1+ = data columns).
    /// </summary>
    public int SortColumnIndex { get; set; } = 0;
    
    public bool SortAscending { get; set; } = true;
    
    public System.Numerics.Vector4? HeaderColor { get; set; }
    
    public System.Numerics.Vector4? EvenRowColor { get; set; }
    
    public System.Numerics.Vector4? OddRowColor { get; set; }
    
    public bool UseFullNameWidth { get; set; } = true;
    
    public bool AutoSizeEqualColumns { get; set; } = false;
    
    public TableHorizontalAlignment HorizontalAlignment { get; set; } = 
        TableHorizontalAlignment.Right;
    
    public TableVerticalAlignment VerticalAlignment { get; set; } = 
        TableVerticalAlignment.Top;
    
    public TableHorizontalAlignment CharacterColumnHorizontalAlignment { get; set; } = 
        TableHorizontalAlignment.Left;
    
    public TableVerticalAlignment CharacterColumnVerticalAlignment { get; set; } = 
        TableVerticalAlignment.Top;
    
    public TableHorizontalAlignment HeaderHorizontalAlignment { get; set; } = 
        TableHorizontalAlignment.Center;
    
    public TableVerticalAlignment HeaderVerticalAlignment { get; set; } = 
        TableVerticalAlignment.Top;
    
    public HashSet<ulong> HiddenCharacters { get; set; } = new();
    
    public bool HideCharacterColumnInAllMode { get; set; } = false;
    
    public List<MergedColumnGroup> MergedColumnGroups { get; set; } = new();
    
    public List<MergedRowGroup> MergedRowGroups { get; set; } = new();
    
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
    
    public bool HideZeroRows { get; set; } = false;
    
    public Models.GraphColorMode ColorMode { get; set; } = Models.GraphColorMode.PreferredItemColors;
    
    /// <summary>Width of the scrollable legend panel.</summary>
    public float LegendWidth { get; set; } = 140f;
    
    /// <summary>Maximum height of the inside legend as a percentage of the graph height.</summary>
    public float LegendHeightPercent { get; set; } = 25f;
    
    public bool ShowLegend { get; set; } = true;
    
    public bool LegendCollapsed { get; set; } = false;
    
    public LegendPosition LegendPosition { get; set; } = 
        LegendPosition.InsideTopLeft;
    
    public GraphType GraphType { get; set; } = GraphType.Stairs;
    
    public bool ShowXAxisTimestamps { get; set; } = true;
    
    public bool ShowCrosshair { get; set; } = true;
    
    public bool ShowGridLines { get; set; } = true;
    
    public bool ShowCurrentPriceLine { get; set; } = true;
    
    /// <summary>Whether to show a value label at the latest point.</summary>
    public bool ShowValueLabel { get; set; } = true;
    
    public float ValueLabelOffsetX { get; set; } = 0f;
    
    public float ValueLabelOffsetY { get; set; } = 0f;
    
    public bool AutoScrollEnabled { get; set; } = false;
    
    public int AutoScrollTimeValue { get; set; } = 1;
    
    public TimeUnit AutoScrollTimeUnit { get; set; } = TimeUnit.Hours;
    
    /// <summary>Position of "now" on the X-axis when auto-scrolling (0-100%).</summary>
    public float AutoScrollNowPosition { get; set; } = 75f;
    
    public bool ShowControlsDrawer { get; set; } = true;
    
    public int TimeRangeValue { get; set; } = 7;
    
    public TimeUnit TimeRangeUnit { get; set; } = TimeUnit.Days;
}
