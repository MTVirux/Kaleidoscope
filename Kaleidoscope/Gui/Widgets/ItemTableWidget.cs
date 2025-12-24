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
    
    // Settings binding
    private IItemTableWidgetSettings? _boundSettings;
    private Action? _onSettingsChanged;
    private string _settingsName = "Table Settings";
    
    // Track if we've applied the initial sort from saved settings
    private bool _sortInitialized = false;
    
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
    public ItemTableWidget(
        TableConfig config,
        ItemDataService? itemDataService = null,
        TrackedDataRegistry? trackedDataRegistry = null)
    {
        _config = config ?? new TableConfig();
        _itemDataService = itemDataService;
        _trackedDataRegistry = trackedDataRegistry;
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
        
        var columnCount = 1 + columns.Count; // Character name + data columns
        
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;
        
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
        
        if (!ImGui.BeginTable(_config.TableId, columnCount, flags))
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
            ImGui.TableSetupColumn("Character", charFlags, charColumnWidth);
            
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var header = GetColumnHeader(column);
                var colFlags = ImGuiTableColumnFlags.PreferSortDescending;
                if (sortColIdx == i + 1)
                {
                    colFlags = savedIsDescending
                        ? ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending
                        : ImGuiTableColumnFlags.DefaultSort;
                }
                // Use equal width if enabled, otherwise use column's configured width
                var colWidth = settings.AutoSizeEqualColumns ? dataColumnWidth : column.Width;
                ImGui.TableSetupColumn(header, colFlags, colWidth);
            }
            ImGui.TableSetupScrollFreeze(0, 1);
            
            // Apply header color if set
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, settings.HeaderColor.Value);
            }
            
            // Draw custom header row with alignment support
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            
            // Character column header
            ImGui.TableNextColumn();
            DrawAlignedHeaderCell(
                "Character",
                settings.HeaderHorizontalAlignment,
                settings.HeaderVerticalAlignment,
                0,
                settings.Sortable);
            
            // Data column headers
            for (int i = 0; i < columns.Count; i++)
            {
                ImGui.TableNextColumn();
                var header = GetColumnHeader(columns[i]);
                DrawAlignedHeaderCell(
                    header,
                    settings.HeaderHorizontalAlignment,
                    settings.HeaderVerticalAlignment,
                    i + 1,
                    settings.Sortable);
            }
            
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PopStyleColor();
            }
            
            // Handle sorting
            var sortedRows = GetSortedRows(rows, columns, settings);
            var useCompact = settings.UseCompactNumbers;
            
            // Draw data rows
            int rowIndex = 0;
            foreach (var row in sortedRows)
            {
                ImGui.TableNextRow();
                
                // Apply row background color based on even/odd
                var isEven = rowIndex % 2 == 0;
                if (isEven && settings.EvenRowColor.HasValue)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.EvenRowColor.Value));
                }
                else if (!isEven && settings.OddRowColor.HasValue)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.OddRowColor.Value));
                }
                rowIndex++;
                
                // Character name
                ImGui.TableNextColumn();
                DrawAlignedCellText(
                    row.Name, 
                    settings.CharacterColumnColor, 
                    settings.CharacterColumnHorizontalAlignment, 
                    settings.CharacterColumnVerticalAlignment);
                
                // Data columns
                for (int i = 0; i < columns.Count; i++)
                {
                    ImGui.TableNextColumn();
                    var column = columns[i];
                    var value = row.ItemCounts.TryGetValue(column.Id, out var count) ? count : 0;
                    DrawAlignedCellText(
                        FormatNumber(value, useCompact), 
                        column.Color, 
                        settings.HorizontalAlignment, 
                        settings.VerticalAlignment);
                }
            }
            
            // Total row
            if (settings.ShowTotalRow && sortedRows.Count > 1)
            {
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(_config.TotalRowColor));
                
                ImGui.TableNextColumn();
                DrawAlignedCellText(
                    "TOTAL", 
                    null, 
                    settings.CharacterColumnHorizontalAlignment, 
                    settings.CharacterColumnVerticalAlignment);
                
                foreach (var column in columns)
                {
                    ImGui.TableNextColumn();
                    var sum = sortedRows.Sum(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0);
                    DrawAlignedCellText(
                        FormatNumber(sum, useCompact), 
                        column.Color, 
                        settings.HorizontalAlignment, 
                        settings.VerticalAlignment);
                }
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
            return rows.OrderBy(r => r.Name).ToList();
        
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
            // Sort by character name
            sorted = sortAscending 
                ? rows.OrderBy(r => r.Name) 
                : rows.OrderByDescending(r => r.Name);
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
            sorted = rows.OrderBy(r => r.Name);
        }
        
        return sorted.ToList();
    }
    
    private static string FormatNumber(long value, bool compact)
    {
        if (!compact)
        {
            return value.ToString("N0");
        }
        
        // Compact notation
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000d:0.##}B",
            >= 1_000_000 => $"{value / 1_000_000d:0.##}M",
            >= 1_000 => $"{value / 1_000d:0.##}K",
            _ => value.ToString("N0")
        };
    }
    
    /// <summary>
    /// Draws text in a table cell with the specified alignment.
    /// </summary>
    private static void DrawAlignedCellText(
        string text, 
        Vector4? color, 
        TableHorizontalAlignment hAlign, 
        TableVerticalAlignment vAlign)
    {
        var textSize = ImGui.CalcTextSize(text);
        var cellSize = ImGui.GetContentRegionAvail();
        var style = ImGui.GetStyle();
        
        // Calculate horizontal offset
        float offsetX = hAlign switch
        {
            TableHorizontalAlignment.Center => (cellSize.X - textSize.X) * 0.5f,
            TableHorizontalAlignment.Right => cellSize.X - textSize.X,
            _ => 0f // Left alignment, no offset
        };
        
        // Calculate vertical offset based on row height
        // Use frame padding Y as an approximation of row height padding
        float offsetY = vAlign switch
        {
            TableVerticalAlignment.Center => (style.CellPadding.Y * 2 + textSize.Y - textSize.Y) * 0.5f - style.CellPadding.Y,
            TableVerticalAlignment.Bottom => style.CellPadding.Y,
            _ => 0f // Top alignment, no offset
        };
        
        // For vertical alignment, we need to adjust the cursor position
        // Since table cells have a fixed height based on content, we use dummy spacing
        if (offsetX > 0f || offsetY != 0f)
        {
            var cursorPos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new Vector2(cursorPos.X + Math.Max(0f, offsetX), cursorPos.Y + offsetY));
        }
        
        if (color.HasValue)
        {
            ImGui.TextColored(color.Value, text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }
    }
    
    /// <summary>
    /// Draws a header cell with alignment and sorting support.
    /// </summary>
    private static void DrawAlignedHeaderCell(
        string label,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        int columnIndex,
        bool sortable)
    {
        var textSize = ImGui.CalcTextSize(label);
        var cellSize = ImGui.GetContentRegionAvail();
        var style = ImGui.GetStyle();
        
        // Reserve space for sort arrow if sortable (approximately 20 pixels)
        const float sortArrowWidth = 20f;
        var effectiveCellWidth = sortable ? cellSize.X - sortArrowWidth : cellSize.X;
        
        // Calculate horizontal offset
        float offsetX = hAlign switch
        {
            TableHorizontalAlignment.Center => (effectiveCellWidth - textSize.X) * 0.5f,
            TableHorizontalAlignment.Right => effectiveCellWidth - textSize.X,
            _ => 0f // Left alignment, no offset
        };
        
        // Calculate vertical offset
        float offsetY = vAlign switch
        {
            TableVerticalAlignment.Center => (style.CellPadding.Y * 2 + textSize.Y - textSize.Y) * 0.5f - style.CellPadding.Y,
            TableVerticalAlignment.Bottom => style.CellPadding.Y,
            _ => 0f // Top alignment, no offset
        };
        
        // Apply offset
        if (offsetX > 0f || offsetY != 0f)
        {
            var cursorPos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new Vector2(cursorPos.X + Math.Max(0f, offsetX), cursorPos.Y + offsetY));
        }
        
        // Use TableHeader to get sorting functionality - this renders the text and handles sort clicks
        ImGui.TableHeader(label);
    }
    
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
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Column Sizing");
        ImGui.Separator();
        
        var useFullNameWidth = settings.UseFullNameWidth;
        if (ImGui.Checkbox("Fit character column to name width", ref useFullNameWidth))
        {
            settings.UseFullNameWidth = useFullNameWidth;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Character column minimum width will be the width of the longest character name.");
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
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Data Column Alignment");
        ImGui.Separator();
        
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
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Character Column Alignment");
        ImGui.Separator();
        
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
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Header Row Alignment");
        ImGui.Separator();
        
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
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Character Column");
        ImGui.Separator();
        
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
        changed |= DrawColorOption("Text Color", settings.CharacterColumnColor, c => settings.CharacterColumnColor = c);
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Row Colors");
        ImGui.Separator();
        
        // Header color
        changed |= DrawColorOption("Header", settings.HeaderColor, c => settings.HeaderColor = c);
        
        // Even row color
        changed |= DrawColorOption("Even Rows", settings.EvenRowColor, c => settings.EvenRowColor = c);
        
        // Odd row color
        changed |= DrawColorOption("Odd Rows", settings.OddRowColor, c => settings.OddRowColor = c);
        
        if (changed)
        {
            _onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws a color option with enable/disable toggle.
    /// </summary>
    private static bool DrawColorOption(string label, Vector4? currentColor, Action<Vector4?> setColor)
    {
        var changed = false;
        var hasColor = currentColor.HasValue;
        var color = currentColor ?? new Vector4(0.3f, 0.3f, 0.3f, 0.5f);
        
        // Enable checkbox
        if (ImGui.Checkbox($"##{label}Enabled", ref hasColor))
        {
            setColor(hasColor ? color : null);
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(hasColor ? "Click to use default color" : "Click to enable custom color");
        }
        
        ImGui.SameLine();
        
        // Color picker (disabled if not enabled)
        ImGui.BeginDisabled(!hasColor);
        if (ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            setColor(color);
            changed = true;
        }
        ImGui.EndDisabled();
        
        return changed;
    }
    
    #endregion
}
