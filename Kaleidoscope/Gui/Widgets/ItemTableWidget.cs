using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;
using MTGui.Common;
using MTGui.Table;
using MTGui.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Grouping mode for table rows.
/// </summary>
public enum TableGroupingMode
{
    /// <summary>No grouping - show each character as a separate row.</summary>
    Character = 0,
    /// <summary>Group by world - aggregate all characters on the same world.</summary>
    World = 1,
    /// <summary>Group by data center - aggregate all characters in the same DC.</summary>
    DataCenter = 2,
    /// <summary>Group by region - aggregate all characters in the same region.</summary>
    Region = 3,
    /// <summary>All combined - show a single row with all data aggregated.</summary>
    All = 4
}

/// <summary>
/// Mode for determining cell text colors in the table.
/// </summary>
public enum TableTextColorMode
{
    /// <summary>Don't use preferred colors - use custom column colors only.</summary>
    DontUse = 0,
    /// <summary>Use preferred item colors from configuration.</summary>
    PreferredItemColors = 1,
    /// <summary>Use preferred character colors from configuration.</summary>
    PreferredCharacterColors = 2
}

/// <summary>
/// Configuration for an item column in the table.
/// </summary>
public class ItemColumnConfig
{
    /// <summary>
    /// Unique identifier for this column (item ID or currency type).
    /// </summary>
    public uint Id { get; set; }
    
    /// <summary>
    /// Display name for the column header. If null, uses the item/currency name.
    /// </summary>
    public string? CustomName { get; set; }
    
    /// <summary>
    /// Whether this column represents a currency (from TrackedDataType) or an inventory item.
    /// </summary>
    public bool IsCurrency { get; set; }
    
    /// <summary>
    /// Custom color for this column's data. If null, uses default text color.
    /// </summary>
    public Vector4? Color { get; set; }
    
    /// <summary>
    /// Column width in pixels. 0 means auto-width.
    /// </summary>
    public float Width { get; set; } = 70f;
    
    /// <summary>
    /// Whether to store historical time-series data for this item.
    /// Only applies to inventory items (not currencies, which are always tracked).
    /// </summary>
    public bool StoreHistory { get; set; } = false;
    
    /// <summary>
    /// Whether to show this item/currency in table view.
    /// </summary>
    public bool ShowInTable { get; set; } = true;
    
    /// <summary>
    /// Whether to show this item/currency in graph view.
    /// </summary>
    public bool ShowInGraph { get; set; } = true;
}

/// <summary>
/// Represents a group of merged columns that display summed values.
/// Extends MTGui's MTMergedColumnGroupBase with Kaleidoscope-specific visibility settings.
/// </summary>
public class MergedColumnGroup : MTGui.Table.MTMergedColumnGroupBase
{
    /// <summary>
    /// Whether to show this merged group in table view.
    /// </summary>
    public bool ShowInTable { get; set; } = true;
    
    /// <summary>
    /// Whether to show this merged group in graph view.
    /// Only applicable when all member items have historical tracking enabled.
    /// </summary>
    public bool ShowInGraph { get; set; } = true;
}

/// <summary>
/// Represents a group of merged rows that display summed values.
/// Supports both Character-mode (CharacterIds) and grouped-mode (GroupKeys).
/// This is FFXIV-specific due to character/world/DC/region grouping concepts.
/// </summary>
public class MergedRowGroup
{
    /// <summary>
    /// Custom display name for the merged row.
    /// </summary>
    public string Name { get; set; } = "Merged";
    
    /// <summary>
    /// List of character IDs that are merged into this row.
    /// Used when GroupingMode is Character.
    /// </summary>
    public List<ulong> CharacterIds { get; set; } = new();
    
    /// <summary>
    /// List of group keys (e.g., world names, DC names, region names) that are merged.
    /// Used when GroupingMode is World, DataCenter, Region, or All.
    /// </summary>
    public List<string> GroupKeys { get; set; } = new();
    
    /// <summary>
    /// The grouping mode this merge group was created under.
    /// Determines whether to use CharacterIds or GroupKeys.
    /// </summary>
    public TableGroupingMode GroupingMode { get; set; } = TableGroupingMode.Character;
    
    /// <summary>
    /// Optional custom color for the merged row name. If null, uses default.
    /// </summary>
    public Vector4? Color { get; set; }
}

/// <summary>
/// Settings interface for the item table widget.
/// Implement this interface in your settings class to enable automatic settings binding.
/// </summary>
public interface IItemTableWidgetSettings
{
    /// <summary>
    /// List of column configurations for items/currencies to display.
    /// </summary>
    List<ItemColumnConfig> Columns { get; set; }
    
    /// <summary>
    /// Whether to show a total row at the bottom summing all characters.
    /// </summary>
    bool ShowTotalRow { get; set; }
    
    /// <summary>
    /// Whether to allow sorting by clicking column headers.
    /// </summary>
    bool Sortable { get; set; }
    
    /// <summary>
    /// Whether to include retainer inventory in item counts.
    /// </summary>
    bool IncludeRetainers { get; set; }
    
    /// <summary>
    /// Width of the character name column.
    /// </summary>
    float CharacterColumnWidth { get; set; }
    
    /// <summary>
    /// Optional custom color for the character name column.
    /// </summary>
    Vector4? CharacterColumnColor { get; set; }
    
    /// <summary>
    /// Number format configuration for displaying values.
    /// </summary>
    NumberFormatConfig NumberFormat { get; set; }
    
    /// <summary>
    /// Index of the column to sort by (0 = character name, 1+ = data columns).
    /// </summary>
    int SortColumnIndex { get; set; }
    
    /// <summary>
    /// Whether to sort in ascending order.
    /// </summary>
    bool SortAscending { get; set; }
    
    /// <summary>
    /// Optional custom color for the table header row.
    /// </summary>
    Vector4? HeaderColor { get; set; }
    
    /// <summary>
    /// Optional custom color for even-numbered rows (0, 2, 4...).
    /// </summary>
    Vector4? EvenRowColor { get; set; }
    
    /// <summary>
    /// Optional custom color for odd-numbered rows (1, 3, 5...).
    /// </summary>
    Vector4? OddRowColor { get; set; }
    
    /// <summary>
    /// Whether to use the full character name width as the minimum column width.
    /// When enabled, the character column will be at least as wide as the longest name.
    /// </summary>
    bool UseFullNameWidth { get; set; }
    
    /// <summary>
    /// Whether to auto-size all data columns to equal widths.
    /// The character column width (based on name width if UseFullNameWidth) takes priority.
    /// </summary>
    bool AutoSizeEqualColumns { get; set; }
    
    /// <summary>
    /// Horizontal alignment for data cell content.
    /// </summary>
    MTTableHorizontalAlignment HorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for data cell content.
    /// </summary>
    MTTableVerticalAlignment VerticalAlignment { get; set; }
    
    /// <summary>
    /// Horizontal alignment for character column content.
    /// </summary>
    MTTableHorizontalAlignment CharacterColumnHorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for character column content.
    /// </summary>
    MTTableVerticalAlignment CharacterColumnVerticalAlignment { get; set; }
    
    /// <summary>
    /// Horizontal alignment for header row content.
    /// </summary>
    MTTableHorizontalAlignment HeaderHorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for header row content.
    /// </summary>
    MTTableVerticalAlignment HeaderVerticalAlignment { get; set; }
    
    /// <summary>
    /// Set of character IDs that are hidden from the table.
    /// </summary>
    HashSet<ulong> HiddenCharacters { get; set; }
    
    /// <summary>
    /// Grouping mode for table rows (Character, World, DataCenter, Region, All).
    /// </summary>
    TableGroupingMode GroupingMode { get; set; }
    
    /// <summary>
    /// Whether to hide the character/group column when GroupingMode is All.
    /// </summary>
    bool HideCharacterColumnInAllMode { get; set; }
    
    /// <summary>
    /// List of merged column groups. Each group combines multiple columns into one.
    /// </summary>
    List<MergedColumnGroup> MergedColumnGroups { get; set; }
    
    /// <summary>
    /// List of merged row groups. Each group combines multiple character rows into one.
    /// </summary>
    List<MergedRowGroup> MergedRowGroups { get; set; }
    
    /// <summary>
    /// Mode for determining cell text colors.
    /// </summary>
    TableTextColorMode TextColorMode { get; set; }
    
    /// <summary>
    /// Whether to show expandable retainer breakdown for characters with retainer data.
    /// When enabled, characters with retainers can be expanded to show per-retainer counts.
    /// </summary>
    bool ShowRetainerBreakdown { get; set; }
    
    /// <summary>
    /// Whether to hide rows where all column values are zero.
    /// </summary>
    bool HideZeroRows { get; set; }
}

/// <summary>
/// Data for a single character row in the table.
/// </summary>
public class ItemTableCharacterRow
{
    /// <summary>
    /// Character ID.
    /// </summary>
    public ulong CharacterId { get; set; }
    
    /// <summary>
    /// Character display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// World name for this character (e.g., "Balmung").
    /// </summary>
    public string WorldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Data center name for this character (e.g., "Crystal").
    /// </summary>
    public string DataCenterName { get; set; } = string.Empty;
    
    /// <summary>
    /// Region name for this character (e.g., "North-America").
    /// </summary>
    public string RegionName { get; set; } = string.Empty;
    
    /// <summary>
    /// Item counts keyed by column ID (item ID or currency type ID).
    /// </summary>
    public Dictionary<uint, long> ItemCounts { get; set; } = new();
    
    /// <summary>
    /// Player-only item counts (excluding retainers) keyed by column ID.
    /// Only populated when ShowRetainerBreakdown is enabled.
    /// </summary>
    public Dictionary<uint, long>? PlayerItemCounts { get; set; }
    
    /// <summary>
    /// Retainer breakdown data: (retainerId, retainerName) -> (columnId -> count).
    /// Only populated when ShowRetainerBreakdown is enabled.
    /// </summary>
    public Dictionary<(ulong RetainerId, string Name), Dictionary<uint, long>>? RetainerBreakdown { get; set; }
    
    /// <summary>
    /// Whether this character has any retainer data.
    /// </summary>
    public bool HasRetainerData => RetainerBreakdown != null && RetainerBreakdown.Count > 0;
}

/// <summary>
/// Prepared data for item table rendering.
/// </summary>
public class PreparedItemTableData
{
    /// <summary>
    /// All character rows to display.
    /// </summary>
    public required IReadOnlyList<ItemTableCharacterRow> Rows { get; init; }
    
    /// <summary>
    /// Column configurations in display order.
    /// </summary>
    public required IReadOnlyList<ItemColumnConfig> Columns { get; init; }
}

/// <summary>
/// A reusable table widget for displaying item/currency quantities across characters.
/// Characters are displayed as rows, items/currencies as columns.
/// Implements ISettingsProvider to expose table settings for automatic inclusion in tool settings.
/// </summary>
public partial class ItemTableWidget : ISettingsProvider
{
    private readonly ItemDataService? _itemDataService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly Configuration? _configuration;
    private readonly TimeSeriesCacheService? _cacheService;
    
    // Settings binding
    private IItemTableWidgetSettings? _boundSettings;
    
    // Column selection state (for shift+click/drag selection)
    private readonly HashSet<int> _selectedColumnIndices = new();
    private readonly HashSet<int> _selectedDisplayColumnIndices = new(); // Tracks display column indices (can include merged)
    private bool _isSelectingColumns = false;
    private int _selectionStartDisplayColumn = -1;
    
    // Row selection state (for shift+click/drag selection)
    private readonly HashSet<ulong> _selectedRowIds = new();
    private readonly HashSet<int> _selectedDisplayRowIndices = new(); // Tracks display row indices (can include merged)
    private bool _isSelectingRows = false;
    private int _selectionStartDisplayRow = -1;
    private List<ulong> _currentRowOrder = new(); // Track row order for range selection
    
    private Action? _onSettingsChanged;
    private string _settingsName = "Table Settings";
    
    // Cached data for character name lookups in settings
    private IReadOnlyList<ItemTableCharacterRow>? _cachedRows;
    
    // Cached display rows for merge operations (refreshed each frame)
    private List<DisplayRow> _cachedDisplayRows = new();
    
    // Cached display columns for merge operations (refreshed each frame)
    private List<DisplayColumn> _cachedDisplayColumns = new();
    
    // Track if we've applied the initial sort from saved settings
    private bool _sortInitialized = false;
    
    // Track if column widths have been initialized (skip first frame to avoid overwriting saved widths)
    private bool _columnWidthsInitialized = false;
    
    // Track if we just processed a merge action (to skip click handling for one frame)
    private bool _skipNextClick = false;
    
    // Track which character rows are expanded to show retainer breakdown
    private readonly HashSet<ulong> _expandedCharacterIds = new();
    
    // Track which grouped rows (by name) are expanded to show retainer breakdown (for World/DC/Region/All modes)
    private readonly HashSet<string> _expandedGroupNames = new();
    
    /// <summary>
    /// Configuration for this table widget instance.
    /// </summary>
    public class TableConfig
    {
        /// <summary>
        /// Unique ID for ImGui table identification.
        /// </summary>
        public string TableId { get; set; } = "ItemTable";
        
        /// <summary>
        /// Text to display when there is no data.
        /// </summary>
        public string NoDataText { get; set; } = "No data available.";
        
        /// <summary>
        /// Default color for data cells when no custom color is set.
        /// </summary>
        public Vector4 DefaultTextColor { get; set; } = new(1f, 1f, 1f, 1f);
        
        /// <summary>
        /// Background color for the total row.
        /// </summary>
        public Vector4 TotalRowColor { get; set; } = new(0.3f, 0.3f, 0.3f, 0.5f);
    }
    
    private readonly TableConfig _config;
    
    public ItemTableWidget(
        TableConfig config,
        ItemDataService? itemDataService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        Configuration? configuration = null,
        TimeSeriesCacheService? cacheService = null)
    {
        _config = config ?? new TableConfig();
        _itemDataService = itemDataService;
        _trackedDataRegistry = trackedDataRegistry;
        _configuration = configuration;
        _cacheService = cacheService;
    }
    
    /// <summary>
    /// Binds this widget to a settings object for automatic synchronization.
    /// </summary>
    public void BindSettings(
        IItemTableWidgetSettings settings,
        Action? onSettingsChanged = null,
        string settingsName = "Table Settings")
    {
        _boundSettings = settings;
        _onSettingsChanged = onSettingsChanged;
        _settingsName = settingsName;
    }
    
    /// <summary>
    /// Gets the column header text for a column configuration.
    /// </summary>
    public string GetColumnHeader(ItemColumnConfig column)
    {
        if (!string.IsNullOrEmpty(column.CustomName))
            return column.CustomName;
        
        if (column.IsCurrency && _trackedDataRegistry != null)
        {
            var dataType = (Models.TrackedDataType)column.Id;
            if (_trackedDataRegistry.Definitions.TryGetValue(dataType, out var def))
                return def.ShortName;
        }
        
        if (!column.IsCurrency && _itemDataService != null)
        {
            return _itemDataService.GetItemName(column.Id) ?? $"Item {column.Id}";
        }
        
        return column.IsCurrency ? $"Currency {column.Id}" : $"Item {column.Id}";
    }
}