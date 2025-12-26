using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Horizontal alignment options for table cell content.
/// </summary>
public enum TableHorizontalAlignment
{
    /// <summary>Align content to the left of the cell.</summary>
    Left,
    /// <summary>Center content horizontally in the cell.</summary>
    Center,
    /// <summary>Align content to the right of the cell.</summary>
    Right
}

/// <summary>
/// Vertical alignment options for table cell content.
/// </summary>
public enum TableVerticalAlignment
{
    /// <summary>Align content to the top of the cell.</summary>
    Top,
    /// <summary>Center content vertically in the cell.</summary>
    Center,
    /// <summary>Align content to the bottom of the cell.</summary>
    Bottom
}

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
}

/// <summary>
/// Represents a group of merged columns that display summed values.
/// </summary>
public class MergedColumnGroup
{
    /// <summary>
    /// Custom display name for the merged column header.
    /// </summary>
    public string Name { get; set; } = "Merged";
    
    /// <summary>
    /// List of column indices (0-based, referencing the Columns list) that are merged.
    /// </summary>
    public List<int> ColumnIndices { get; set; } = new();
    
    /// <summary>
    /// Optional custom color for the merged column. If null, uses default.
    /// </summary>
    public Vector4? Color { get; set; }
    
    /// <summary>
    /// Width of the merged column in pixels.
    /// </summary>
    public float Width { get; set; } = 80f;
}

/// <summary>
/// Represents a group of merged rows that display summed values.
/// </summary>
public class MergedRowGroup
{
    /// <summary>
    /// Custom display name for the merged row.
    /// </summary>
    public string Name { get; set; } = "Merged";
    
    /// <summary>
    /// List of character IDs that are merged into this row.
    /// </summary>
    public List<ulong> CharacterIds { get; set; } = new();
    
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
    /// Whether to use compact number notation (e.g., 10M instead of 10,000,000).
    /// </summary>
    bool UseCompactNumbers { get; set; }
    
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
    TableHorizontalAlignment HorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for data cell content.
    /// </summary>
    TableVerticalAlignment VerticalAlignment { get; set; }
    
    /// <summary>
    /// Horizontal alignment for character column content.
    /// </summary>
    TableHorizontalAlignment CharacterColumnHorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for character column content.
    /// </summary>
    TableVerticalAlignment CharacterColumnVerticalAlignment { get; set; }
    
    /// <summary>
    /// Horizontal alignment for header row content.
    /// </summary>
    TableHorizontalAlignment HeaderHorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for header row content.
    /// </summary>
    TableVerticalAlignment HeaderVerticalAlignment { get; set; }
    
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
public class ItemTableWidget : ISettingsProvider
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
    
    /// <summary>
    /// Creates a new ItemTableWidget.
    /// </summary>
    /// <param name="config">Configuration for the table.</param>
    /// <param name="itemDataService">Optional item data service for name lookups.</param>
    /// <param name="trackedDataRegistry">Optional registry for currency name lookups.</param>
    /// <param name="configuration">Optional configuration for accessing preferred colors.</param>
    /// <param name="cacheService">Optional cache service for accessing character colors.</param>
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
    /// <param name="settings">The settings object implementing IItemTableWidgetSettings.</param>
    /// <param name="onSettingsChanged">Callback when settings are changed (e.g., to trigger config save).</param>
    /// <param name="settingsName">Display name for the settings section.</param>
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
    
    /// <summary>
    /// Represents a column to display, either a single column or a merged group.
    /// </summary>
    private class DisplayColumn
    {
        public bool IsMerged { get; init; }
        public string Header { get; init; } = string.Empty;
        public float Width { get; init; }
        public Vector4? Color { get; init; }
        /// <summary>Original column indices in the Columns list that this display column represents.</summary>
        public List<int> SourceColumnIndices { get; init; } = new();
        /// <summary>The merged group reference (if IsMerged is true).</summary>
        public MergedColumnGroup? MergedGroup { get; init; }
    }
    
    /// <summary>
    /// Builds the list of display columns, combining individual columns and merged groups.
    /// </summary>
    private List<DisplayColumn> BuildDisplayColumns(IReadOnlyList<ItemColumnConfig> columns, IItemTableWidgetSettings settings, float autoWidth)
    {
        var displayColumns = new List<DisplayColumn>();
        var mergedIndices = new HashSet<int>();
        
        // First, collect all indices that are part of a merged group
        foreach (var group in settings.MergedColumnGroups)
        {
            foreach (var idx in group.ColumnIndices)
            {
                mergedIndices.Add(idx);
            }
        }
        
        // Track which merged groups we've already added (by their first column index)
        var addedMergedGroups = new HashSet<int>();
        
        // Iterate through all columns in order
        for (int i = 0; i < columns.Count; i++)
        {
            if (mergedIndices.Contains(i))
            {
                // This column is part of a merged group - find which one
                var group = settings.MergedColumnGroups.FirstOrDefault(g => g.ColumnIndices.Contains(i));
                if (group != null)
                {
                    // Only add the merged group once (at the position of its first column)
                    var firstIdx = group.ColumnIndices.Min();
                    if (i == firstIdx && !addedMergedGroups.Contains(firstIdx))
                    {
                        addedMergedGroups.Add(firstIdx);
                        displayColumns.Add(new DisplayColumn
                        {
                            IsMerged = true,
                            Header = group.Name,
                            Width = settings.AutoSizeEqualColumns ? autoWidth : group.Width,
                            Color = group.Color,
                            SourceColumnIndices = group.ColumnIndices.ToList(),
                            MergedGroup = group
                        });
                    }
                    // Skip other columns in the same merged group
                }
            }
            else
            {
                // Regular column (not merged)
                var column = columns[i];
                displayColumns.Add(new DisplayColumn
                {
                    IsMerged = false,
                    Header = GetColumnHeader(column),
                    Width = settings.AutoSizeEqualColumns ? autoWidth : column.Width,
                    Color = column.Color,
                    SourceColumnIndices = new List<int> { i },
                    MergedGroup = null
                });
            }
        }
        
        return displayColumns;
    }
    
    /// <summary>
    /// Calculates the summed value for a display column from a character row.
    /// </summary>
    private static long GetDisplayColumnValue(DisplayColumn displayCol, ItemTableCharacterRow row, IReadOnlyList<ItemColumnConfig> columns)
    {
        long sum = 0;
        foreach (var idx in displayCol.SourceColumnIndices)
        {
            if (idx >= 0 && idx < columns.Count)
            {
                var colId = columns[idx].Id;
                if (row.ItemCounts.TryGetValue(colId, out var count))
                {
                    sum += count;
                }
            }
        }
        return sum;
    }
    
    /// <summary>
    /// Represents a row to display, either a single character or a merged group.
    /// </summary>
    private class DisplayRow
    {
        public bool IsMerged { get; init; }
        public string Name { get; init; } = string.Empty;
        public Vector4? Color { get; init; }
        /// <summary>Character IDs that this display row represents.</summary>
        public List<ulong> SourceCharacterIds { get; init; } = new();
        /// <summary>The merged group reference (if IsMerged is true).</summary>
        public MergedRowGroup? MergedGroup { get; init; }
        /// <summary>Aggregated item counts from all source rows.</summary>
        public Dictionary<uint, long> ItemCounts { get; init; } = new();
    }
    
    /// <summary>
    /// Builds the list of display rows, combining individual rows and merged groups.
    /// </summary>
    private List<DisplayRow> BuildDisplayRows(IReadOnlyList<ItemTableCharacterRow> rows, IItemTableWidgetSettings settings, IReadOnlyList<ItemColumnConfig> columns)
    {
        var displayRows = new List<DisplayRow>();
        var mergedCharacterIds = new HashSet<ulong>();
        
        // First, collect all character IDs that are part of a merged group
        foreach (var group in settings.MergedRowGroups)
        {
            foreach (var cid in group.CharacterIds)
            {
                mergedCharacterIds.Add(cid);
            }
        }
        
        // Track which merged groups we've already added
        var addedMergedGroups = new HashSet<MergedRowGroup>();
        
        // Iterate through all rows in order
        foreach (var row in rows)
        {
            if (mergedCharacterIds.Contains(row.CharacterId))
            {
                // This row is part of a merged group - find which one
                var group = settings.MergedRowGroups.FirstOrDefault(g => g.CharacterIds.Contains(row.CharacterId));
                if (group != null && !addedMergedGroups.Contains(group))
                {
                    addedMergedGroups.Add(group);
                    
                    // Aggregate item counts from all characters in this merged group
                    var aggregatedCounts = new Dictionary<uint, long>();
                    foreach (var cid in group.CharacterIds)
                    {
                        var sourceRow = rows.FirstOrDefault(r => r.CharacterId == cid);
                        if (sourceRow != null)
                        {
                            foreach (var kvp in sourceRow.ItemCounts)
                            {
                                if (aggregatedCounts.TryGetValue(kvp.Key, out var existing))
                                    aggregatedCounts[kvp.Key] = existing + kvp.Value;
                                else
                                    aggregatedCounts[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    
                    displayRows.Add(new DisplayRow
                    {
                        IsMerged = true,
                        Name = group.Name,
                        Color = group.Color,
                        SourceCharacterIds = group.CharacterIds.ToList(),
                        MergedGroup = group,
                        ItemCounts = aggregatedCounts
                    });
                }
                // Skip other rows in the same merged group
            }
            else
            {
                // Regular row (not merged)
                displayRows.Add(new DisplayRow
                {
                    IsMerged = false,
                    Name = row.Name,
                    Color = null,
                    SourceCharacterIds = new List<ulong> { row.CharacterId },
                    MergedGroup = null,
                    ItemCounts = row.ItemCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                });
            }
        }
        
        return displayRows;
    }
    
    /// <summary>
    /// Calculates the summed value for a display column from a display row.
    /// </summary>
    private static long GetDisplayValue(DisplayColumn displayCol, DisplayRow displayRow, IReadOnlyList<ItemColumnConfig> columns)
    {
        long sum = 0;
        foreach (var idx in displayCol.SourceColumnIndices)
        {
            if (idx >= 0 && idx < columns.Count)
            {
                var colId = columns[idx].Id;
                if (displayRow.ItemCounts.TryGetValue(colId, out var count))
                {
                    sum += count;
                }
            }
        }
        return sum;
    }
    
    /// <summary>
    /// Gets the set of currently selected column indices (data columns only, 0-indexed).
    /// </summary>
    public IReadOnlySet<int> SelectedColumnIndices => _selectedColumnIndices;
    
    /// <summary>
    /// Gets the set of currently selected row character IDs.
    /// </summary>
    public IReadOnlySet<ulong> SelectedRowIds => _selectedRowIds;
    
    /// <summary>
    /// Gets the effective color for a column based on the TextColorMode setting.
    /// </summary>
    /// <param name="column">The column config (may be null for merged columns).</param>
    /// <param name="displayCol">The display column.</param>
    /// <param name="settings">The table settings.</param>
    /// <param name="columns">All column configurations.</param>
    /// <returns>The effective color to use, or null if no color should be applied.</returns>
    private Vector4? GetEffectiveColumnColor(ItemColumnConfig? column, DisplayColumn displayCol, IItemTableWidgetSettings settings, IReadOnlyList<ItemColumnConfig> columns)
    {
        // If text color mode is DontUse, just return the custom column color
        if (settings.TextColorMode == TableTextColorMode.DontUse)
            return displayCol.Color;
        
        // PreferredItemColors mode - use item colors from configuration
        if (settings.TextColorMode == TableTextColorMode.PreferredItemColors && _configuration != null)
        {
            // For merged columns, use the first source column's item/currency
            var sourceIdx = displayCol.SourceColumnIndices.FirstOrDefault(-1);
            if (sourceIdx >= 0 && sourceIdx < columns.Count)
            {
                var sourceCol = columns[sourceIdx];
                if (sourceCol.IsCurrency)
                {
                    // Check ItemColors (TrackedDataType -> uint)
                    var dataType = (Models.TrackedDataType)sourceCol.Id;
                    if (_configuration.ItemColors.TryGetValue(dataType, out var colorUint))
                        return UintToVector4(colorUint);
                }
                else
                {
                    // Check GameItemColors (item ID -> uint)
                    if (_configuration.GameItemColors.TryGetValue(sourceCol.Id, out var colorUint))
                        return UintToVector4(colorUint);
                }
            }
        }
        
        // Fallback to custom column color if preferred color not found
        return displayCol.Color;
    }
    
    /// <summary>
    /// Gets the effective color for a character/row based on the TextColorMode setting.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="settings">The table settings.</param>
    /// <param name="fallbackColor">The fallback color to use (e.g., row color or character column color).</param>
    /// <returns>The effective color to use, or null if no color should be applied.</returns>
    private Vector4? GetEffectiveCharacterColor(ulong characterId, IItemTableWidgetSettings settings, Vector4? fallbackColor)
    {
        // If text color mode is PreferredCharacterColors, use character colors from cache
        if (settings.TextColorMode == TableTextColorMode.PreferredCharacterColors && _cacheService != null)
        {
            var charColor = _cacheService.GetCharacterTimeSeriesColor(characterId);
            if (charColor.HasValue)
                return UintToVector4(charColor.Value);
        }
        
        return fallbackColor;
    }
    
    /// <summary>
    /// Converts a uint color (ABGR format from ImGui) to Vector4.
    /// </summary>
    private static Vector4 UintToVector4(uint color)
    {
        var r = (color & 0xFF) / 255f;
        var g = ((color >> 8) & 0xFF) / 255f;
        var b = ((color >> 16) & 0xFF) / 255f;
        var a = ((color >> 24) & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }
    
    /// <summary>
    /// Draws the item table.
    /// </summary>
    /// <param name="data">The prepared table data to display.</param>
    /// <param name="settings">Optional settings override. If null, uses bound settings.</param>
    public void Draw(PreparedItemTableData? data, IItemTableWidgetSettings? settings = null)
    {
        settings ??= _boundSettings;
        if (settings == null || data == null)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }
        
        // Handle selection state based on SHIFT key
        var isShiftHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        
        // Check if any popup is currently open (to avoid clearing selection when clicking menu items)
        var isPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
        
        // Skip click processing if we just handled a merge action
        if (_skipNextClick)
        {
            _skipNextClick = false;
        }
        // Clear selection when clicking without SHIFT (but not when a popup is open)
        // We keep selection when SHIFT is released so user can right-click to merge
        else if (!isShiftHeld && !isPopupOpen && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectedColumnIndices.Clear();
            _selectedDisplayColumnIndices.Clear();
            _isSelectingColumns = false;
            _selectionStartDisplayColumn = -1;
            
            _selectedRowIds.Clear();
            _selectedDisplayRowIndices.Clear();
            _isSelectingRows = false;
            _selectionStartDisplayRow = -1;
        }
        
        // Cache rows for character name lookups in settings
        _cachedRows = data.Rows;
        
        var columns = data.Columns;
        if (columns.Count == 0)
        {
            ImGui.TextUnformatted("No columns configured. Add items or currencies in settings.");
            return;
        }
        
        var rows = data.Rows;
        if (rows.Count == 0)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }
        
        // Determine if we should hide the character column
        var hideCharColumn = settings.GroupingMode == TableGroupingMode.All && settings.HideCharacterColumnInAllMode;
        
        // Calculate character column width based on longest name if UseFullNameWidth is enabled
        var charColumnWidth = settings.CharacterColumnWidth;
        if (settings.UseFullNameWidth && rows.Count > 0)
        {
            var maxNameWidth = 0f;
            foreach (var row in rows)
            {
                var nameWidth = ImGui.CalcTextSize(row.Name).X;
                if (nameWidth > maxNameWidth)
                    maxNameWidth = nameWidth;
            }
            // Add padding for cell margins, borders, and extra safety margin
            maxNameWidth += ImGui.GetStyle().CellPadding.X;
            // Use the larger of calculated width or configured minimum
            charColumnWidth = Math.Max(charColumnWidth, maxNameWidth);
        }
        
        // Calculate equal width for data columns if AutoSizeEqualColumns is enabled
        float dataColumnWidth = 0f;
        if (settings.AutoSizeEqualColumns && columns.Count > 0)
        {
            // Get available width after accounting for character column and borders
            var availableWidth = ImGui.GetContentRegionAvail().X;
            // Subtract character column width and some margin for borders/scrollbar
            var remainingWidth = availableWidth - charColumnWidth - 20f;
            dataColumnWidth = Math.Max(50f, remainingWidth / columns.Count);
        }
        
        // Build display columns (handles merged columns)
        var displayColumns = BuildDisplayColumns(columns, settings, dataColumnWidth);
        _cachedDisplayColumns = displayColumns; // Cache for merge operations
        var displayColumnCount = hideCharColumn ? displayColumns.Count : 1 + displayColumns.Count;
        
        // NoSavedSettings prevents ImGui from trying to use its internal ini persistence
        // (Dalamud doesn't support ImGui ini files for plugins - we save column widths ourselves)
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;
        
        if (!ImGui.BeginTable(_config.TableId, displayColumnCount, flags))
            return;
        
        try
        {
            // Setup columns - apply DefaultSort flag to the saved sort column
            // PreferSortDescending makes first click sort descending (more useful for quantities)
            // For the default sort column, only use PreferSortDescending if saved state is descending
            // This ensures the arrow direction matches the actual sort order on reload
            var sortColIdx = settings.SortColumnIndex;
            var savedIsDescending = !settings.SortAscending;
            
            var charFlags = ImGuiTableColumnFlags.PreferSortDescending;
            if (sortColIdx == 0)
            {
                charFlags = savedIsDescending 
                    ? ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending
                    : ImGuiTableColumnFlags.DefaultSort;
            }
            // When auto-sizing is enabled, use WidthFixed for character column so it only shrinks as a last resort
            if (settings.AutoSizeEqualColumns)
            {
                charFlags |= ImGuiTableColumnFlags.WidthFixed;
            }
            
            // Only setup character column if not hidden
            if (!hideCharColumn)
            {
                ImGui.TableSetupColumn("Character", charFlags, charColumnWidth);
            }
            
            // Setup display columns (includes merged columns)
            for (int i = 0; i < displayColumns.Count; i++)
            {
                var displayCol = displayColumns[i];
                var colFlags = ImGuiTableColumnFlags.PreferSortDescending;
                if (sortColIdx == i + 1)
                {
                    colFlags = savedIsDescending
                        ? ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending
                        : ImGuiTableColumnFlags.DefaultSort;
                }
                ImGui.TableSetupColumn(displayCol.Header, colFlags, displayCol.Width);
            }
            ImGui.TableSetupScrollFreeze(0, 1);
            
            // Apply header color if set
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, settings.HeaderColor.Value);
            }
            
            // Draw custom header row with alignment support
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            
            // Character column header (only if not hidden)
            if (!hideCharColumn)
            {
                ImGui.TableNextColumn();
                DrawAlignedHeaderCell(
                    "Character",
                    settings.HeaderHorizontalAlignment,
                    settings.HeaderVerticalAlignment,
                    0,
                    settings.Sortable);
            }
            
            // Data column headers (using display columns which include merged columns)
            for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
            {
                ImGui.TableNextColumn();
                var displayCol = displayColumns[dispIdx];
                
                // Handle header selection with SHIFT+click/drag
                // Check if this display column is selected
                var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                if (isShiftHeld && !isPopupOpen) // Allow selecting both merged and non-merged columns
                {
                    // Get the header bounds for hover/click detection
                    var cellMin = ImGui.GetCursorScreenPos();
                    var cellMax = new Vector2(cellMin.X + ImGui.GetContentRegionAvail().X, cellMin.Y + ImGui.GetTextLineHeightWithSpacing());
                    var isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                    
                    // Start selection on click
                    if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _isSelectingColumns = true;
                        _selectionStartDisplayColumn = dispIdx;
                        _selectedDisplayColumnIndices.Clear();
                        _selectedDisplayColumnIndices.Add(dispIdx);
                    }
                    
                    // Extend selection while dragging
                    if (_isSelectingColumns && ImGui.IsMouseDown(ImGuiMouseButton.Left) && isHovered)
                    {
                        var minCol = Math.Min(_selectionStartDisplayColumn, dispIdx);
                        var maxCol = Math.Max(_selectionStartDisplayColumn, dispIdx);
                        _selectedDisplayColumnIndices.Clear();
                        for (int col = minCol; col <= maxCol; col++)
                        {
                            _selectedDisplayColumnIndices.Add(col);
                        }
                    }
                    
                    // End selection on mouse release
                    if (_isSelectingColumns && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        _isSelectingColumns = false;
                    }
                    
                    isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                }
                
                // Apply highlight background for selected headers
                if (isColumnSelected)
                {
                    var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f);
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(highlightColor));
                }
                
                DrawAlignedHeaderCell(
                    displayCol.Header,
                    settings.HeaderHorizontalAlignment,
                    settings.HeaderVerticalAlignment,
                    hideCharColumn ? dispIdx : dispIdx + 1,
                    settings.Sortable);
            }
            
            // Right-click context menu for column merging (when display columns are selected)
            if (_selectedDisplayColumnIndices.Count >= 2)
            {
                // Create a unique popup ID for this widget instance
                var popupId = $"MergeColumnsPopup_{_config.TableId}";
                
                // Check if right-click happened anywhere in the table area
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                {
                    ImGui.OpenPopup(popupId);
                }
                
                if (ImGui.BeginPopup(popupId))
                {
                    ImGui.TextDisabled($"{_selectedDisplayColumnIndices.Count} columns selected");
                    ImGui.Separator();
                    
                    if (ImGui.MenuItem("Merge Selected Columns"))
                    {
                        // Collect all source column indices from selected display columns
                        // This flattens any existing merged groups
                        var allSourceIndices = new HashSet<int>();
                        var mergedGroupsToRemove = new List<MergedColumnGroup>();
                        
                        foreach (var dispIdx in _selectedDisplayColumnIndices)
                        {
                            if (dispIdx >= 0 && dispIdx < displayColumns.Count)
                            {
                                var displayCol = displayColumns[dispIdx];
                                foreach (var srcIdx in displayCol.SourceColumnIndices)
                                {
                                    allSourceIndices.Add(srcIdx);
                                }
                                
                                // Track merged groups that need to be removed
                                if (displayCol.IsMerged && displayCol.MergedGroup != null)
                                {
                                    mergedGroupsToRemove.Add(displayCol.MergedGroup);
                                }
                            }
                        }
                        
                        // Remove old merged groups that were consumed
                        foreach (var oldGroup in mergedGroupsToRemove)
                        {
                            settings.MergedColumnGroups.Remove(oldGroup);
                        }
                        
                        // Create a new merged column group with all source indices
                        var mergedGroup = new MergedColumnGroup
                        {
                            Name = "Merged",
                            ColumnIndices = allSourceIndices.OrderBy(x => x).ToList(),
                            Width = 80f
                        };
                        
                        settings.MergedColumnGroups.Add(mergedGroup);
                        _selectedDisplayColumnIndices.Clear();
                        _selectedColumnIndices.Clear();
                        _skipNextClick = true; // Prevent click from selecting underneath
                        _onSettingsChanged?.Invoke();
                    }
                    
                    ImGui.EndPopup();
                }
            }
            
            // Right-click context menu for row merging (when display rows are selected)
            if (_selectedDisplayRowIndices.Count >= 2)
            {
                var rowPopupId = $"MergeRowsPopup_{_config.TableId}";
                
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                {
                    ImGui.OpenPopup(rowPopupId);
                }
                
                if (ImGui.BeginPopup(rowPopupId))
                {
                    ImGui.TextDisabled($"{_selectedDisplayRowIndices.Count} rows selected");
                    ImGui.Separator();
                    
                    if (ImGui.MenuItem("Merge Selected Rows"))
                    {
                        // Collect all source character IDs from selected display rows
                        // This flattens any existing merged groups
                        var allCharacterIds = new HashSet<ulong>();
                        var mergedGroupsToRemove = new List<MergedRowGroup>();
                        
                        foreach (var dispRowIdx in _selectedDisplayRowIndices)
                        {
                            if (dispRowIdx >= 0 && dispRowIdx < _cachedDisplayRows.Count)
                            {
                                var displayRow = _cachedDisplayRows[dispRowIdx];
                                foreach (var cid in displayRow.SourceCharacterIds)
                                {
                                    allCharacterIds.Add(cid);
                                }
                                
                                // Track merged groups that need to be removed
                                if (displayRow.IsMerged && displayRow.MergedGroup != null)
                                {
                                    mergedGroupsToRemove.Add(displayRow.MergedGroup);
                                }
                            }
                        }
                        
                        // Remove old merged groups that were consumed
                        foreach (var oldGroup in mergedGroupsToRemove)
                        {
                            settings.MergedRowGroups.Remove(oldGroup);
                        }
                        
                        // Create a new merged row group with all character IDs
                        var mergedGroup = new MergedRowGroup
                        {
                            Name = "Merged",
                            CharacterIds = allCharacterIds.OrderBy(x => x).ToList()
                        };
                        
                        settings.MergedRowGroups.Add(mergedGroup);
                        _selectedDisplayRowIndices.Clear();
                        _selectedRowIds.Clear();
                        _skipNextClick = true; // Prevent click from selecting row underneath
                        _onSettingsChanged?.Invoke();
                    }
                    
                    ImGui.EndPopup();
                }
            }
            
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PopStyleColor();
            }
            
            // Handle sorting
            var sortedRows = GetSortedRows(rows, columns, settings);
            var useCompact = settings.UseCompactNumbers;
            
            // Filter out hidden characters
            var visibleRows = sortedRows.Where(r => !settings.HiddenCharacters.Contains(r.CharacterId)).ToList();
            
            // Apply grouping if not in Character mode
            var groupedRows = ApplyGrouping(visibleRows, columns, settings.GroupingMode);
            
            // Build display rows (handles merged rows)
            var finalDisplayRows = BuildDisplayRows(groupedRows, settings, columns);
            _cachedDisplayRows = finalDisplayRows; // Cache for merge operations
            
            // Track row order for range selection
            _currentRowOrder = finalDisplayRows
                .Where(r => !r.IsMerged)
                .SelectMany(r => r.SourceCharacterIds)
                .ToList();
            
            // Determine if we should show character context menu (only in Character mode)
            var showCharContextMenu = settings.GroupingMode == TableGroupingMode.Character;
            
            // Draw data rows
            int rowIndex = 0;
            for (int dispRowIdx = 0; dispRowIdx < finalDisplayRows.Count; dispRowIdx++)
            {
                var dispRow = finalDisplayRows[dispRowIdx];
                ImGui.TableNextRow();
                
                // Check if this display row is selected
                var isRowSelected = _selectedDisplayRowIndices.Contains(dispRowIdx);
                
                // Apply row background color based on even/odd or selection
                var isEven = rowIndex % 2 == 0;
                if (isRowSelected)
                {
                    var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f);
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(highlightColor));
                }
                else if (isEven && settings.EvenRowColor.HasValue)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.EvenRowColor.Value));
                }
                else if (!isEven && settings.OddRowColor.HasValue)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.OddRowColor.Value));
                }
                rowIndex++;
                
                // Character name with selection and context menu (only if not hidden)
                if (!hideCharColumn)
                {
                    ImGui.TableNextColumn();
                    var primaryCid = dispRow.SourceCharacterIds.FirstOrDefault();
                    ImGui.PushID((int)primaryCid);
                    
                    // Handle row selection with SHIFT+click/drag on character column
                    // Allow selecting both merged and non-merged rows
                    if (isShiftHeld && !isPopupOpen)
                    {
                        var cellMin = ImGui.GetCursorScreenPos();
                        var cellMax = new Vector2(cellMin.X + ImGui.GetContentRegionAvail().X, cellMin.Y + ImGui.GetTextLineHeightWithSpacing());
                        var isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                        
                        // Start selection on click
                        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            _isSelectingRows = true;
                            _selectionStartDisplayRow = dispRowIdx;
                            _selectedDisplayRowIndices.Clear();
                            _selectedDisplayRowIndices.Add(dispRowIdx);
                        }
                        
                        // Extend selection while dragging
                        if (_isSelectingRows && ImGui.IsMouseDown(ImGuiMouseButton.Left) && isHovered)
                        {
                            var minIdx = Math.Min(_selectionStartDisplayRow, dispRowIdx);
                            var maxIdx = Math.Max(_selectionStartDisplayRow, dispRowIdx);
                            _selectedDisplayRowIndices.Clear();
                            for (int idx = minIdx; idx <= maxIdx; idx++)
                            {
                                _selectedDisplayRowIndices.Add(idx);
                            }
                        }
                        
                        // End selection on mouse release
                        if (_isSelectingRows && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            _isSelectingRows = false;
                        }
                        
                        isRowSelected = _selectedDisplayRowIndices.Contains(dispRowIdx);
                    }
                    
                    // Determine text color - use preferred character colors if enabled
                    Vector4? nameColor = GetEffectiveCharacterColor(primaryCid, settings, dispRow.Color ?? settings.CharacterColumnColor);
                    if (isRowSelected)
                    {
                        var baseColor = nameColor ?? _config.DefaultTextColor;
                        nameColor = new Vector4(1f - baseColor.X, 1f - baseColor.Y, 1f - baseColor.Z, baseColor.W);
                    }
                    
                    DrawAlignedCellText(
                        dispRow.Name, 
                        nameColor, 
                        settings.CharacterColumnHorizontalAlignment, 
                        settings.CharacterColumnVerticalAlignment);
                    
                    // Right-click context menu on character name (only in Character mode for non-merged rows)
                    if (showCharContextMenu && !dispRow.IsMerged && ImGui.BeginPopupContextItem($"CharContext_{primaryCid}"))
                    {
                        ImGui.TextDisabled(dispRow.Name);
                        ImGui.Separator();
                        
                        if (ImGui.MenuItem("Hide Character"))
                        {
                            settings.HiddenCharacters.Add(primaryCid);
                            _onSettingsChanged?.Invoke();
                        }
                        
                        ImGui.EndPopup();
                    }
                    
                    ImGui.PopID();
                }
                
                // Data columns (using display columns which include merged columns)
                for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
                {
                    ImGui.TableNextColumn();
                    var displayCol = displayColumns[dispIdx];
                    var value = GetDisplayValue(displayCol, dispRow, columns);
                    
                    // Handle column selection with SHIFT+click/drag
                    // Check if this display column is selected
                    var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                    if (isShiftHeld && !isPopupOpen) // Allow selecting both merged and non-merged columns
                    {
                        // Get the column bounds for hover/click detection
                        var cellMin = ImGui.GetCursorScreenPos();
                        var cellMax = new Vector2(cellMin.X + ImGui.GetContentRegionAvail().X, cellMin.Y + ImGui.GetTextLineHeightWithSpacing());
                        var isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                        
                        // Start selection on click
                        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            _isSelectingColumns = true;
                            _selectionStartDisplayColumn = dispIdx;
                            _selectedDisplayColumnIndices.Clear();
                            _selectedDisplayColumnIndices.Add(dispIdx);
                        }
                        
                        // Extend selection while dragging
                        if (_isSelectingColumns && ImGui.IsMouseDown(ImGuiMouseButton.Left) && isHovered)
                        {
                            var minCol = Math.Min(_selectionStartDisplayColumn, dispIdx);
                            var maxCol = Math.Max(_selectionStartDisplayColumn, dispIdx);
                            _selectedDisplayColumnIndices.Clear();
                            for (int col = minCol; col <= maxCol; col++)
                            {
                                _selectedDisplayColumnIndices.Add(col);
                            }
                        }
                        
                        // End selection on mouse release
                        if (_isSelectingColumns && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            _isSelectingColumns = false;
                        }
                        
                        // Update selection status after potential changes
                        isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                    }
                    
                    // Apply inverted background color for selected columns
                    if (isColumnSelected)
                    {
                        // Use inverted/highlight color for selected cell background
                        var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f); // Yellow-ish highlight
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(highlightColor));
                    }
                    
                    // Determine text color - use preferred item colors if enabled, then invert if selected
                    var sourceColIdx = displayCol.SourceColumnIndices.FirstOrDefault(-1);
                    var sourceCol = sourceColIdx >= 0 && sourceColIdx < columns.Count ? columns[sourceColIdx] : null;
                    Vector4? textColor = GetEffectiveColumnColor(sourceCol, displayCol, settings, columns);
                    if (isColumnSelected)
                    {
                        // Invert the color for selected columns
                        var baseColor = textColor ?? _config.DefaultTextColor;
                        textColor = new Vector4(1f - baseColor.X, 1f - baseColor.Y, 1f - baseColor.Z, baseColor.W);
                    }
                    
                    DrawAlignedCellText(
                        FormatNumber(value, useCompact), 
                        textColor, 
                        settings.HorizontalAlignment, 
                        settings.VerticalAlignment);
                }
            }
            
            // Total row (only show if there are multiple display rows and not in All mode)
            // In All mode, the single row already shows the total
            if (settings.ShowTotalRow && finalDisplayRows.Count > 1 && settings.GroupingMode != TableGroupingMode.All)
            {
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(_config.TotalRowColor));
                
                if (!hideCharColumn)
                {
                    ImGui.TableNextColumn();
                    DrawAlignedCellText(
                        "TOTAL", 
                        null, 
                        settings.CharacterColumnHorizontalAlignment, 
                        settings.CharacterColumnVerticalAlignment);
                }
                
                for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
                {
                    ImGui.TableNextColumn();
                    var displayCol = displayColumns[dispIdx];
                    var sum = finalDisplayRows.Sum(r => GetDisplayValue(displayCol, r, columns));
                    
                    // Apply selection styling to total row as well
                    var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                    if (isColumnSelected)
                    {
                        var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f);
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(highlightColor));
                    }
                    
                    // Use preferred item colors for total row as well
                    var totalSourceColIdx = displayCol.SourceColumnIndices.FirstOrDefault(-1);
                    var totalSourceCol = totalSourceColIdx >= 0 && totalSourceColIdx < columns.Count ? columns[totalSourceColIdx] : null;
                    Vector4? textColor = GetEffectiveColumnColor(totalSourceCol, displayCol, settings, columns);
                    if (isColumnSelected)
                    {
                        var baseColor = textColor ?? _config.DefaultTextColor;
                        textColor = new Vector4(1f - baseColor.X, 1f - baseColor.Y, 1f - baseColor.Z, baseColor.W);
                    }
                    
                    DrawAlignedCellText(
                        FormatNumber(sum, useCompact), 
                        textColor, 
                        settings.HorizontalAlignment, 
                        settings.VerticalAlignment);
                }
            }
            
            // Capture and save column widths if user has resized them
            // Skip the first frame to avoid overwriting saved widths with defaults
            if (_columnWidthsInitialized)
            {
                var widthsChanged = false;
                
                // Get column widths by navigating to each column and reading content region
                // This works because we're still inside the table context
                
                // Check character column width (column 0) - only if not hidden
                if (!hideCharColumn)
                {
                    ImGui.TableSetColumnIndex(0);
                    var currentCharWidth = ImGui.GetContentRegionAvail().X;
                    // Add cell padding back to get the actual column width
                    currentCharWidth += ImGui.GetStyle().CellPadding.X * 2;
                    if (Math.Abs(currentCharWidth - settings.CharacterColumnWidth) > 1f)
                    {
                        settings.CharacterColumnWidth = currentCharWidth;
                        widthsChanged = true;
                    }
                }
                
                // Check display column widths (includes merged columns)
                var dataColOffset = hideCharColumn ? 0 : 1;
                for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
                {
                    ImGui.TableSetColumnIndex(dispIdx + dataColOffset);
                    var currentWidth = ImGui.GetContentRegionAvail().X;
                    currentWidth += ImGui.GetStyle().CellPadding.X * 2;
                    
                    var displayCol = displayColumns[dispIdx];
                    if (displayCol.IsMerged && displayCol.MergedGroup != null)
                    {
                        // Update merged group width
                        if (Math.Abs(currentWidth - displayCol.MergedGroup.Width) > 1f)
                        {
                            displayCol.MergedGroup.Width = currentWidth;
                            widthsChanged = true;
                        }
                    }
                    else if (!displayCol.IsMerged && displayCol.SourceColumnIndices.Count == 1)
                    {
                        // Update regular column width
                        var colIdx = displayCol.SourceColumnIndices[0];
                        if (colIdx >= 0 && colIdx < columns.Count && Math.Abs(currentWidth - columns[colIdx].Width) > 1f)
                        {
                            columns[colIdx].Width = currentWidth;
                            widthsChanged = true;
                        }
                    }
                }
                
                if (widthsChanged)
                {
                    _onSettingsChanged?.Invoke();
                }
            }
            else
            {
                _columnWidthsInitialized = true;
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }
    
    private List<ItemTableCharacterRow> GetSortedRows(
        IReadOnlyList<ItemTableCharacterRow> rows,
        IReadOnlyList<ItemColumnConfig> columns,
        IItemTableWidgetSettings settings)
    {
        if (!settings.Sortable)
            return rows.ToList(); // Preserve order from caller (already sorted by config)
        
        // Check for sort specs - update settings when user changes sort
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            // On first frame, SpecsDirty is true but we should use saved settings
            // Only save if this isn't the initial sort setup
            if (_sortInitialized && sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                settings.SortColumnIndex = spec.ColumnIndex;
                settings.SortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
                _onSettingsChanged?.Invoke();
            }
            _sortInitialized = true;
            sortSpecs.SpecsDirty = false;
        }
        
        var sortColumnIndex = settings.SortColumnIndex;
        var sortAscending = settings.SortAscending;
        
        // Sort the rows
        IEnumerable<ItemTableCharacterRow> sorted;
        if (sortColumnIndex == 0)
        {
            // Sort by character name column - preserve the order from caller (already sorted by config)
            // If descending, reverse the pre-sorted order to maintain AR/alphabetical order in reverse
            sorted = sortAscending 
                ? rows // Preserve order from caller (already sorted by CharacterSortHelper)
                : rows.Reverse(); // Reverse the configured order (could be AR order, alphabetical, etc.)
        }
        else if (sortColumnIndex > 0 && sortColumnIndex <= columns.Count)
        {
            // Sort by data column
            var column = columns[sortColumnIndex - 1];
            sorted = sortAscending
                ? rows.OrderBy(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0)
                : rows.OrderByDescending(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0);
        }
        else
        {
            sorted = rows; // Preserve order from caller
        }
        
        return sorted.ToList();
    }
    
    /// <summary>
    /// Applies grouping to the rows based on the selected grouping mode.
    /// </summary>
    private static List<ItemTableCharacterRow> ApplyGrouping(
        IReadOnlyList<ItemTableCharacterRow> rows,
        IReadOnlyList<ItemColumnConfig> columns,
        TableGroupingMode mode)
    {
        if (mode == TableGroupingMode.Character || rows.Count == 0)
        {
            // No grouping - return as-is
            return rows.ToList();
        }
        
        if (mode == TableGroupingMode.All)
        {
            // Combine all rows into a single aggregate row
            var aggregateRow = new ItemTableCharacterRow
            {
                CharacterId = 0,
                Name = "All Characters",
                WorldName = string.Empty,
                DataCenterName = string.Empty,
                RegionName = string.Empty,
                ItemCounts = new Dictionary<uint, long>()
            };
            
            foreach (var column in columns)
            {
                var sum = rows.Sum(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0);
                aggregateRow.ItemCounts[column.Id] = sum;
            }
            
            return new List<ItemTableCharacterRow> { aggregateRow };
        }
        
        // Group by the selected field
        Func<ItemTableCharacterRow, string> keySelector = mode switch
        {
            TableGroupingMode.World => r => string.IsNullOrEmpty(r.WorldName) ? "Unknown World" : r.WorldName,
            TableGroupingMode.DataCenter => r => string.IsNullOrEmpty(r.DataCenterName) ? "Unknown DC" : r.DataCenterName,
            TableGroupingMode.Region => r => string.IsNullOrEmpty(r.RegionName) ? "Unknown Region" : r.RegionName,
            _ => r => r.Name
        };
        
        var grouped = rows.GroupBy(keySelector);
        var result = new List<ItemTableCharacterRow>();
        
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            var aggregateRow = new ItemTableCharacterRow
            {
                // Use 0 as character ID for grouped rows (no single character)
                CharacterId = 0,
                Name = group.Key,
                WorldName = mode == TableGroupingMode.World ? group.Key : group.First().WorldName,
                DataCenterName = mode == TableGroupingMode.DataCenter ? group.Key : group.First().DataCenterName,
                RegionName = mode == TableGroupingMode.Region ? group.Key : group.First().RegionName,
                ItemCounts = new Dictionary<uint, long>()
            };
            
            foreach (var column in columns)
            {
                var sum = group.Sum(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0);
                aggregateRow.ItemCounts[column.Id] = sum;
            }
            
            result.Add(aggregateRow);
        }
        
        return result;
    }
    
    private static string FormatNumber(long value, bool compact) => TableHelpers.FormatNumber(value, compact);
    
    /// <summary>
    /// Draws text in a table cell with the specified alignment.
    /// </summary>
    private static void DrawAlignedCellText(
        string text, 
        Vector4? color, 
        TableHorizontalAlignment hAlign, 
        TableVerticalAlignment vAlign) => TableHelpers.DrawAlignedCellText(text, hAlign, vAlign, color);
    
    /// <summary>
    /// Draws a header cell with alignment and sorting support.
    /// </summary>
    private static void DrawAlignedHeaderCell(
        string label,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        int columnIndex,
        bool sortable) => TableHelpers.DrawAlignedHeaderCell(label, hAlign, vAlign, sortable);
    
    #region ISettingsProvider Implementation
    
    /// <inheritdoc/>
    public bool HasSettings => _boundSettings != null;
    
    /// <inheritdoc/>
    public string SettingsName => _settingsName;
    
    /// <inheritdoc/>
    public bool DrawSettings()
    {
        if (_boundSettings == null) return false;
        
        var changed = false;
        var settings = _boundSettings;
        
        // Table options
        var showTotalRow = settings.ShowTotalRow;
        if (ImGui.Checkbox("Show total row", ref showTotalRow))
        {
            settings.ShowTotalRow = showTotalRow;
            changed = true;
        }
        
        var sortable = settings.Sortable;
        if (ImGui.Checkbox("Enable sorting", ref sortable))
        {
            settings.Sortable = sortable;
            changed = true;
        }
        
        var includeRetainers = settings.IncludeRetainers;
        if (ImGui.Checkbox("Include retainer inventory", ref includeRetainers))
        {
            settings.IncludeRetainers = includeRetainers;
            changed = true;
        }
        
        // Grouping mode
        var groupingMode = (int)settings.GroupingMode;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Group By", ref groupingMode, "Character\0World\0Data Center\0Region\0All\0"))
        {
            settings.GroupingMode = (TableGroupingMode)groupingMode;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Group rows by character, world, data center, region, or combine all into one row.\nWorld/DC/Region grouping requires character world information from AutoRetainer.");
        }
        
        // Text color mode
        var textColorMode = (int)settings.TextColorMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Text Color Mode", ref textColorMode, "Don't use\0Use preferred item colors\0Use preferred character colors\0"))
        {
            settings.TextColorMode = (TableTextColorMode)textColorMode;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Determines how cell text colors are applied.\n- Don't use: Only custom column colors are used.\n- Preferred item colors: Use colors from Data > Currencies and Data > Game Items config.\n- Preferred character colors: Use colors from Data > Characters config.");
        }
        
        // Show hide character column option only in All mode
        if (settings.GroupingMode == TableGroupingMode.All)
        {
            var hideCharColumn = settings.HideCharacterColumnInAllMode;
            if (ImGui.Checkbox("Hide character column", ref hideCharColumn))
            {
                settings.HideCharacterColumnInAllMode = hideCharColumn;
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Hide the 'All Characters' column when grouping mode is All.");
            }
        }
        
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Column Sizing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var useFullNameWidth = settings.UseFullNameWidth;
        if (ImGui.Checkbox("Fit character column to name width", ref useFullNameWidth))
        {
            settings.UseFullNameWidth = useFullNameWidth;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("First column width will be the width of the entry name.");
        }
        
        var autoSizeEqualColumns = settings.AutoSizeEqualColumns;
        if (ImGui.Checkbox("Equal width data columns", ref autoSizeEqualColumns))
        {
            settings.AutoSizeEqualColumns = autoSizeEqualColumns;
            changed = true;
        }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Automatically size all data columns to equal widths.\nCharacter column width takes priority.");
            }
            ImGui.TreePop();
        }
        
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Data Column Alignment"))
        {
            // Data horizontal alignment
            var hAlign = (int)settings.HorizontalAlignment;
            if (ImGui.Combo("Data Horizontal", ref hAlign, "Left\0Center\0Right\0"))
            {
                settings.HorizontalAlignment = (TableHorizontalAlignment)hAlign;
                changed = true;
            }
        
            // Data vertical alignment
            var vAlign = (int)settings.VerticalAlignment;
            if (ImGui.Combo("Data Vertical", ref vAlign, "Top\0Center\0Bottom\0"))
            {
                settings.VerticalAlignment = (TableVerticalAlignment)vAlign;
                changed = true;
            }
            ImGui.TreePop();
        }
        
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Character Column Alignment"))
        {
            // Character column horizontal alignment
            var charHAlign = (int)settings.CharacterColumnHorizontalAlignment;
            if (ImGui.Combo("Character Horizontal", ref charHAlign, "Left\0Center\0Right\0"))
            {
                settings.CharacterColumnHorizontalAlignment = (TableHorizontalAlignment)charHAlign;
                changed = true;
            }
        
            // Character column vertical alignment
            var charVAlign = (int)settings.CharacterColumnVerticalAlignment;
            if (ImGui.Combo("Character Vertical", ref charVAlign, "Top\0Center\0Bottom\0"))
            {
                settings.CharacterColumnVerticalAlignment = (TableVerticalAlignment)charVAlign;
                changed = true;
            }
            ImGui.TreePop();
        }
        
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Header Row Alignment"))
        {
            // Header horizontal alignment
            var headerHAlign = (int)settings.HeaderHorizontalAlignment;
            if (ImGui.Combo("Header Horizontal", ref headerHAlign, "Left\0Center\0Right\0"))
            {
                settings.HeaderHorizontalAlignment = (TableHorizontalAlignment)headerHAlign;
                changed = true;
            }
        
            // Header vertical alignment
            var headerVAlign = (int)settings.HeaderVerticalAlignment;
            if (ImGui.Combo("Header Vertical", ref headerVAlign, "Top\0Center\0Bottom\0"))
            {
                settings.HeaderVerticalAlignment = (TableVerticalAlignment)headerVAlign;
                changed = true;
            }
            ImGui.TreePop();
        }
        
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Character Column"))
        {
            var charWidth = settings.CharacterColumnWidth;
            if (ImGui.SliderFloat("Min Width", ref charWidth, 60f, 200f, "%.0f"))
            {
                settings.CharacterColumnWidth = charWidth;
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Minimum width of the character column.\nIf 'Fit to name width' is enabled, this is the minimum value.");
            }
        
            // Character column color
            changed |= TableHelpers.DrawColorOption("Text Color", settings.CharacterColumnColor, c => settings.CharacterColumnColor = c);
            ImGui.TreePop();
        }
        
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Row Colors"))
        {
            // Header color
            changed |= TableHelpers.DrawColorOption("Header", settings.HeaderColor, c => settings.HeaderColor = c);
        
            // Even row color
            changed |= TableHelpers.DrawColorOption("Even Rows", settings.EvenRowColor, c => settings.EvenRowColor = c);
        
            // Odd row color
            changed |= TableHelpers.DrawColorOption("Odd Rows", settings.OddRowColor, c => settings.OddRowColor = c);
            ImGui.TreePop();
        }
        
        // Merged columns section
        if (settings.MergedColumnGroups.Count > 0)
        {
            ImGui.Spacing();
            
            if (ImGui.TreeNodeEx($"Merged Columns ({settings.MergedColumnGroups.Count})###MergedCols"))
            {
                ImGui.TextDisabled("Hold SHIFT and click/drag on column headers to select, then right-click to merge.");
                ImGui.Spacing();
                
                int? groupToRemove = null;
                for (int i = 0; i < settings.MergedColumnGroups.Count; i++)
                {
                    var group = settings.MergedColumnGroups[i];
                    ImGui.PushID(i);
                    
                    // Unmerge button
                    if (ImGui.SmallButton("Unmerge"))
                    {
                        groupToRemove = i;
                    }
                    ImGui.SameLine();
                    
                    // Editable name
                    ImGui.SetNextItemWidth(100);
                    var name = group.Name;
                    if (ImGui.InputText("##Name", ref name, 64))
                    {
                        group.Name = name;
                        changed = true;
                    }
                    ImGui.SameLine();
                    
                    // Show which columns are merged
                    var columnNames = new List<string>();
                    foreach (var colIdx in group.ColumnIndices)
                    {
                        if (colIdx >= 0 && colIdx < settings.Columns.Count)
                        {
                            columnNames.Add(GetColumnHeader(settings.Columns[colIdx]));
                        }
                    }
                    ImGui.TextDisabled($"({string.Join(" + ", columnNames)})");
                    
                    // Color option
                    var hasColor = group.Color.HasValue;
                    var color = group.Color ?? new Vector4(1f, 1f, 1f, 1f);
                    if (ImGui.Checkbox("##ColorEnabled", ref hasColor))
                    {
                        group.Color = hasColor ? color : null;
                        changed = true;
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(hasColor ? "Click to use default color" : "Click to enable custom color");
                    }
                    ImGui.SameLine();
                    ImGui.BeginDisabled(!hasColor);
                    if (ImGui.ColorEdit4("Color##MergedColor", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
                    {
                        group.Color = color;
                        changed = true;
                    }
                    ImGui.EndDisabled();
                    
                    ImGui.PopID();
                }
                
                // Handle removal after iteration
                if (groupToRemove.HasValue)
                {
                    settings.MergedColumnGroups.RemoveAt(groupToRemove.Value);
                    changed = true;
                }
                
                ImGui.Spacing();
                if (ImGui.Button("Unmerge All"))
                {
                    settings.MergedColumnGroups.Clear();
                    changed = true;
                }
                ImGui.TreePop();
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Hold SHIFT and click/drag on column headers to select, then right-click to merge.");
        }
        
        // Merged rows section
        if (settings.MergedRowGroups.Count > 0)
        {
            ImGui.Spacing();
            
            if (ImGui.TreeNodeEx($"Merged Rows ({settings.MergedRowGroups.Count})###MergedRows"))
            {
                ImGui.TextDisabled("Hold SHIFT and click/drag on character names to select, then right-click to merge.");
                ImGui.Spacing();
                
                int? groupToRemove = null;
                for (int i = 0; i < settings.MergedRowGroups.Count; i++)
                {
                    var group = settings.MergedRowGroups[i];
                    ImGui.PushID($"rowgroup_{i}");
                    
                    // Unmerge button
                    if (ImGui.SmallButton("Unmerge"))
                    {
                        groupToRemove = i;
                    }
                    ImGui.SameLine();
                    
                    // Editable name
                    ImGui.SetNextItemWidth(100);
                    var name = group.Name;
                    if (ImGui.InputText("##Name", ref name, 64))
                    {
                        group.Name = name;
                        changed = true;
                    }
                    ImGui.SameLine();
                    
                    // Show which characters are merged
                    var charNames = new List<string>();
                    foreach (var cid in group.CharacterIds)
                    {
                        var charName = _cachedRows?.FirstOrDefault(r => r.CharacterId == cid)?.Name ?? $"CID: {cid}";
                        charNames.Add(charName);
                    }
                    ImGui.TextDisabled($"({string.Join(" + ", charNames)})");
                    
                    // Color option
                    var hasColor = group.Color.HasValue;
                    var color = group.Color ?? new Vector4(1f, 1f, 1f, 1f);
                    if (ImGui.Checkbox("##ColorEnabled", ref hasColor))
                    {
                        group.Color = hasColor ? color : null;
                        changed = true;
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(hasColor ? "Click to use default color" : "Click to enable custom color");
                    }
                    ImGui.SameLine();
                    ImGui.BeginDisabled(!hasColor);
                    if (ImGui.ColorEdit4("Color##MergedRowColor", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
                    {
                        group.Color = color;
                        changed = true;
                    }
                    ImGui.EndDisabled();
                    
                    ImGui.PopID();
                }
                
                // Handle removal after iteration
                if (groupToRemove.HasValue)
                {
                    settings.MergedRowGroups.RemoveAt(groupToRemove.Value);
                    changed = true;
                }
                
                ImGui.Spacing();
                if (ImGui.Button("Unmerge All Rows"))
                {
                    settings.MergedRowGroups.Clear();
                    changed = true;
                }
                ImGui.TreePop();
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Hold SHIFT and click/drag on character names to select, then right-click to merge.");
        }
        
        // Hidden characters section
        if (settings.HiddenCharacters.Count > 0)
        {
            ImGui.Spacing();
            
            if (ImGui.TreeNodeEx($"Hidden Characters ({settings.HiddenCharacters.Count})###HiddenChars"))
            {
                // Show each hidden character with unhide button
                ulong? characterToUnhide = null;
                foreach (var cid in settings.HiddenCharacters)
                {
                    // Try to find character name from cached data
                    var charName = _cachedRows?.FirstOrDefault(r => r.CharacterId == cid)?.Name ?? $"CID: {cid}";
                    
                    ImGui.PushID((int)cid);
                    if (ImGui.SmallButton("Show"))
                    {
                        characterToUnhide = cid;
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(charName);
                    ImGui.PopID();
                }
                
                // Handle unhide after iteration
                if (characterToUnhide.HasValue)
                {
                    settings.HiddenCharacters.Remove(characterToUnhide.Value);
                    changed = true;
                }
                
                ImGui.Spacing();
                if (ImGui.Button("Show All Characters"))
                {
                    settings.HiddenCharacters.Clear();
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Unhide all hidden characters");
                }
                ImGui.TreePop();
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Right-click a character name to hide them from this table.");
        }
        
        if (changed)
        {
            _onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    #endregion
}
