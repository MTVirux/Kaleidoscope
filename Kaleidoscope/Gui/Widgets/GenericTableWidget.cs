using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Interfaces;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Settings interface for the generic table widget.
/// Implement this interface in your settings class to enable automatic settings binding.
/// </summary>
public interface IGenericTableSettings
{
    /// <summary>
    /// Whether to allow sorting by clicking column headers.
    /// </summary>
    bool Sortable { get; set; }
    
    /// <summary>
    /// Index of the column to sort by.
    /// </summary>
    int SortColumnIndex { get; set; }
    
    /// <summary>
    /// Whether to sort in ascending order.
    /// </summary>
    bool SortAscending { get; set; }
    
    /// <summary>
    /// Optional custom color for the table header row background.
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
    /// Horizontal alignment for data cell content.
    /// </summary>
    TableHorizontalAlignment DataHorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for data cell content.
    /// </summary>
    TableVerticalAlignment DataVerticalAlignment { get; set; }
    
    /// <summary>
    /// Horizontal alignment for header row content.
    /// </summary>
    TableHorizontalAlignment HeaderHorizontalAlignment { get; set; }
    
    /// <summary>
    /// Vertical alignment for header row content.
    /// </summary>
    TableVerticalAlignment HeaderVerticalAlignment { get; set; }
    
    /// <summary>
    /// Whether to use alternating row colors.
    /// </summary>
    bool UseAlternatingRowColors { get; set; }
    
    /// <summary>
    /// Whether to freeze the header row when scrolling.
    /// </summary>
    bool FreezeHeader { get; set; }
}

/// <summary>
/// Default implementation of IGenericTableSettings.
/// </summary>
public class GenericTableSettings : IGenericTableSettings
{
    /// <inheritdoc/>
    public bool Sortable { get; set; } = true;
    
    /// <inheritdoc/>
    public int SortColumnIndex { get; set; } = 0;
    
    /// <inheritdoc/>
    public bool SortAscending { get; set; } = true;
    
    /// <inheritdoc/>
    public Vector4? HeaderColor { get; set; }
    
    /// <inheritdoc/>
    public Vector4? EvenRowColor { get; set; }
    
    /// <inheritdoc/>
    public Vector4? OddRowColor { get; set; }
    
    /// <inheritdoc/>
    public TableHorizontalAlignment DataHorizontalAlignment { get; set; } = TableHorizontalAlignment.Left;
    
    /// <inheritdoc/>
    public TableVerticalAlignment DataVerticalAlignment { get; set; } = TableVerticalAlignment.Top;
    
    /// <inheritdoc/>
    public TableHorizontalAlignment HeaderHorizontalAlignment { get; set; } = TableHorizontalAlignment.Left;
    
    /// <inheritdoc/>
    public TableVerticalAlignment HeaderVerticalAlignment { get; set; } = TableVerticalAlignment.Top;
    
    /// <inheritdoc/>
    public bool UseAlternatingRowColors { get; set; } = true;
    
    /// <inheritdoc/>
    public bool FreezeHeader { get; set; } = true;
}

/// <summary>
/// Column definition for a generic table.
/// </summary>
public class GenericTableColumn
{
    /// <summary>
    /// Column header text.
    /// </summary>
    public required string Header { get; init; }
    
    /// <summary>
    /// Column width. If 0, uses auto-width (stretch).
    /// </summary>
    public float Width { get; init; } = 0f;
    
    /// <summary>
    /// Whether this column should stretch to fill available space.
    /// </summary>
    public bool Stretch { get; init; } = false;
    
    /// <summary>
    /// ImGui column flags. Defaults to WidthFixed unless Stretch is true.
    /// </summary>
    public ImGuiTableColumnFlags Flags { get; init; } = ImGuiTableColumnFlags.None;
    
    /// <summary>
    /// Optional custom color for this column's header text.
    /// </summary>
    public Vector4? HeaderColor { get; init; }
    
    /// <summary>
    /// Whether this column prefers descending sort on first click.
    /// Useful for numeric columns where higher values are typically more interesting.
    /// </summary>
    public bool PreferSortDescending { get; init; } = false;
}

/// <summary>
/// Context passed to cell rendering delegates.
/// </summary>
public class CellRenderContext
{
    /// <summary>
    /// The row index (0-based, after sorting).
    /// </summary>
    public required int RowIndex { get; init; }
    
    /// <summary>
    /// The column index (0-based).
    /// </summary>
    public required int ColumnIndex { get; init; }
    
    /// <summary>
    /// Whether this is an even row (for alternating colors).
    /// </summary>
    public bool IsEvenRow => RowIndex % 2 == 0;
    
    /// <summary>
    /// The table settings.
    /// </summary>
    public required IGenericTableSettings Settings { get; init; }
}

/// <summary>
/// A generic, reusable table widget with customizable columns, sorting, and styling.
/// This widget handles table structure, sorting, and row styling, while delegating
/// cell content rendering to the caller via delegates.
/// </summary>
/// <typeparam name="TRow">The type of data for each row.</typeparam>
public class GenericTableWidget<TRow> : ISettingsProvider
{
    private readonly string _tableId;
    private readonly string _noDataText;
    
    // Settings binding
    private IGenericTableSettings? _boundSettings;
    private Action? _onSettingsChanged;
    private string _settingsName = "Table Settings";
    
    // Sort state tracking
    private bool _sortInitialized = false;
    
    /// <summary>
    /// Delegate for rendering a cell's content.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="context">The cell render context with row/column indices.</param>
    public delegate void CellRenderer(TRow row, CellRenderContext context);
    
    /// <summary>
    /// Delegate for getting a sortable value from a row for a specific column.
    /// Return IComparable (string, int, float, DateTime, etc.) for sorting.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="columnIndex">The column index.</param>
    /// <returns>A comparable value for sorting, or null if not sortable.</returns>
    public delegate IComparable? SortKeySelector(TRow row, int columnIndex);
    
    /// <summary>
    /// Creates a new GenericTableWidget.
    /// </summary>
    /// <param name="tableId">Unique ID for ImGui table identification.</param>
    /// <param name="noDataText">Text to display when there is no data.</param>
    public GenericTableWidget(string tableId, string noDataText = "No data available.")
    {
        _tableId = tableId;
        _noDataText = noDataText;
    }
    
    /// <summary>
    /// Binds this widget to a settings object for automatic synchronization.
    /// </summary>
    /// <param name="settings">The settings object implementing IGenericTableSettings.</param>
    /// <param name="onSettingsChanged">Callback when settings are changed (e.g., to trigger config save).</param>
    /// <param name="settingsName">Display name for the settings section.</param>
    public void BindSettings(
        IGenericTableSettings settings,
        Action? onSettingsChanged = null,
        string settingsName = "Table Settings")
    {
        _boundSettings = settings;
        _onSettingsChanged = onSettingsChanged;
        _settingsName = settingsName;
    }
    
    /// <summary>
    /// Draws the table.
    /// </summary>
    /// <param name="columns">Column definitions.</param>
    /// <param name="rows">Row data.</param>
    /// <param name="cellRenderer">Delegate to render each cell's content.</param>
    /// <param name="sortKeySelector">Optional delegate to get sort keys. If null, sorting uses row order.</param>
    /// <param name="settings">Optional settings override. If null, uses bound settings.</param>
    /// <param name="height">Optional explicit height. If 0, uses available height.</param>
    public void Draw(
        IReadOnlyList<GenericTableColumn> columns,
        IReadOnlyList<TRow> rows,
        CellRenderer cellRenderer,
        SortKeySelector? sortKeySelector = null,
        IGenericTableSettings? settings = null,
        float height = 0f)
    {
        settings ??= _boundSettings ?? new GenericTableSettings();
        
        if (columns.Count == 0)
        {
            ImGui.TextUnformatted("No columns defined.");
            return;
        }
        
        if (rows.Count == 0)
        {
            ImGui.TextUnformatted(_noDataText);
            return;
        }
        
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;
        
        var tableHeight = height > 0 ? height : ImGui.GetContentRegionAvail().Y;
        
        if (!ImGui.BeginTable(_tableId, columns.Count, flags, new Vector2(0, tableHeight)))
            return;
        
        try
        {
            // Setup columns
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var colFlags = column.Flags;
                
                // Apply default sort to saved column
                if (i == settings.SortColumnIndex)
                {
                    colFlags |= settings.SortAscending 
                        ? ImGuiTableColumnFlags.DefaultSort 
                        : ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending;
                }
                else if (column.PreferSortDescending)
                {
                    colFlags |= ImGuiTableColumnFlags.PreferSortDescending;
                }
                
                // Apply width/stretch flags
                if (column.Stretch)
                {
                    colFlags |= ImGuiTableColumnFlags.WidthStretch;
                }
                else if (column.Width > 0)
                {
                    colFlags |= ImGuiTableColumnFlags.WidthFixed;
                }
                
                ImGui.TableSetupColumn(column.Header, colFlags, column.Width);
            }
            
            if (settings.FreezeHeader)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
            }
            
            // Apply header color if set
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, settings.HeaderColor.Value);
            }
            
            // Draw custom header row with alignment support
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            for (int i = 0; i < columns.Count; i++)
            {
                ImGui.TableNextColumn();
                DrawAlignedHeaderCell(
                    columns[i].Header,
                    settings.HeaderHorizontalAlignment,
                    settings.HeaderVerticalAlignment,
                    settings.Sortable,
                    columns[i].HeaderColor);
            }
            
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PopStyleColor();
            }
            
            // Handle sorting
            var sortedRows = GetSortedRows(rows, sortKeySelector, settings);
            
            // Draw data rows
            for (int rowIdx = 0; rowIdx < sortedRows.Count; rowIdx++)
            {
                var row = sortedRows[rowIdx];
                ImGui.TableNextRow();
                
                // Apply row background color based on even/odd
                var isEven = rowIdx % 2 == 0;
                if (settings.UseAlternatingRowColors)
                {
                    if (isEven && settings.EvenRowColor.HasValue)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.EvenRowColor.Value));
                    }
                    else if (!isEven && settings.OddRowColor.HasValue)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.OddRowColor.Value));
                    }
                }
                
                // Render each cell
                for (int colIdx = 0; colIdx < columns.Count; colIdx++)
                {
                    ImGui.TableNextColumn();
                    
                    var context = new CellRenderContext
                    {
                        RowIndex = rowIdx,
                        ColumnIndex = colIdx,
                        Settings = settings
                    };
                    
                    cellRenderer(row, context);
                }
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }
    
    private List<TRow> GetSortedRows(
        IReadOnlyList<TRow> rows,
        SortKeySelector? sortKeySelector,
        IGenericTableSettings settings)
    {
        if (!settings.Sortable || sortKeySelector == null)
            return rows.ToList();
        
        // Check for sort specs - update settings when user changes sort
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
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
        
        // Sort the rows using the sort key selector
        var sorted = rows.ToList();
        sorted.Sort((a, b) =>
        {
            var keyA = sortKeySelector(a, sortColumnIndex);
            var keyB = sortKeySelector(b, sortColumnIndex);
            
            if (keyA == null && keyB == null) return 0;
            if (keyA == null) return sortAscending ? -1 : 1;
            if (keyB == null) return sortAscending ? 1 : -1;
            
            var result = keyA.CompareTo(keyB);
            return sortAscending ? result : -result;
        });
        
        return sorted;
    }
    
    /// <summary>
    /// Draws a header cell with alignment and optional color.
    /// </summary>
    private static void DrawAlignedHeaderCell(
        string label,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        bool sortable,
        Vector4? color)
    {
        var textSize = ImGui.CalcTextSize(label);
        var cellSize = ImGui.GetContentRegionAvail();
        var style = ImGui.GetStyle();
        
        // Reserve space for sort arrow if sortable
        const float sortArrowWidth = 20f;
        var effectiveCellWidth = sortable ? cellSize.X - sortArrowWidth : cellSize.X;
        
        // Calculate horizontal offset
        float offsetX = hAlign switch
        {
            TableHorizontalAlignment.Center => (effectiveCellWidth - textSize.X) * 0.5f,
            TableHorizontalAlignment.Right => effectiveCellWidth - textSize.X,
            _ => 0f
        };
        
        // Calculate vertical offset
        float offsetY = vAlign switch
        {
            TableVerticalAlignment.Center => (style.CellPadding.Y * 2 + textSize.Y - textSize.Y) * 0.5f - style.CellPadding.Y,
            TableVerticalAlignment.Bottom => style.CellPadding.Y,
            _ => 0f
        };
        
        if (offsetX > 0f || offsetY != 0f)
        {
            var cursorPos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new Vector2(cursorPos.X + Math.Max(0f, offsetX), cursorPos.Y + offsetY));
        }
        
        if (color.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
        }
        
        ImGui.TableHeader(label);
        
        if (color.HasValue)
        {
            ImGui.PopStyleColor();
        }
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
        var sortable = settings.Sortable;
        if (ImGui.Checkbox("Enable sorting", ref sortable))
        {
            settings.Sortable = sortable;
            changed = true;
        }
        
        var freezeHeader = settings.FreezeHeader;
        if (ImGui.Checkbox("Freeze header row", ref freezeHeader))
        {
            settings.FreezeHeader = freezeHeader;
            changed = true;
        }
        
        var useAlternatingColors = settings.UseAlternatingRowColors;
        if (ImGui.Checkbox("Use alternating row colors", ref useAlternatingColors))
        {
            settings.UseAlternatingRowColors = useAlternatingColors;
            changed = true;
        }
        
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Data Column Alignment", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Data horizontal alignment
            var hAlign = (int)settings.DataHorizontalAlignment;
            if (ImGui.Combo("Data Horizontal", ref hAlign, "Left\0Center\0Right\0"))
            {
                settings.DataHorizontalAlignment = (TableHorizontalAlignment)hAlign;
                changed = true;
            }
        
            // Data vertical alignment
            var vAlign = (int)settings.DataVerticalAlignment;
            if (ImGui.Combo("Data Vertical", ref vAlign, "Top\0Center\0Bottom\0"))
            {
                settings.DataVerticalAlignment = (TableVerticalAlignment)vAlign;
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
        if (ImGui.TreeNodeEx("Row Colors"))
        {
            // Header color
            changed |= DrawColorOption("Header", settings.HeaderColor, c => settings.HeaderColor = c);
        
            // Even row color
            changed |= DrawColorOption("Even Rows", settings.EvenRowColor, c => settings.EvenRowColor = c);
        
            // Odd row color
            changed |= DrawColorOption("Odd Rows", settings.OddRowColor, c => settings.OddRowColor = c);
            ImGui.TreePop();
        }
        
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
    
    #region Helper Methods for Cell Rendering
    
    /// <summary>
    /// Helper method to draw text with alignment in a cell.
    /// Call this from your cell renderer delegate for aligned text.
    /// </summary>
    public static void DrawAlignedText(
        string text,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        Vector4? color = null)
    {
        TableHelpers.DrawAlignedCellText(text, hAlign, vAlign, color);
    }
    
    /// <summary>
    /// Helper method to draw text using settings alignment.
    /// </summary>
    public static void DrawAlignedText(string text, IGenericTableSettings settings, Vector4? color = null)
    {
        TableHelpers.DrawAlignedCellText(text, settings.DataHorizontalAlignment, settings.DataVerticalAlignment, color);
    }
    
    #endregion
}

/// <summary>
/// Static helper methods for table rendering that can be used by any table implementation.
/// These provide common functionality like aligned cell rendering, standard table flags, and color options.
/// </summary>
public static class TableHelpers
{
    /// <summary>
    /// Gets the standard table flags used across the application.
    /// </summary>
    /// <param name="sortable">Whether the table should be sortable.</param>
    /// <param name="scrollable">Whether the table should have vertical scrolling.</param>
    /// <returns>Combined ImGuiTableFlags.</returns>
    public static ImGuiTableFlags GetStandardTableFlags(bool sortable = false, bool scrollable = true)
    {
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
        if (scrollable) flags |= ImGuiTableFlags.ScrollY;
        if (sortable) flags |= ImGuiTableFlags.Sortable;
        return flags;
    }
    
    /// <summary>
    /// Draws text in a table cell with the specified alignment.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="hAlign">Horizontal alignment.</param>
    /// <param name="vAlign">Vertical alignment.</param>
    /// <param name="color">Optional text color.</param>
    public static void DrawAlignedCellText(
        string text,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        Vector4? color = null)
    {
        var textSize = ImGui.CalcTextSize(text);
        var cellSize = ImGui.GetContentRegionAvail();
        var style = ImGui.GetStyle();
        
        float offsetX = hAlign switch
        {
            TableHorizontalAlignment.Center => (cellSize.X - textSize.X) * 0.5f,
            TableHorizontalAlignment.Right => cellSize.X - textSize.X,
            _ => 0f
        };
        
        float offsetY = vAlign switch
        {
            TableVerticalAlignment.Center => (style.CellPadding.Y * 2 + textSize.Y - textSize.Y) * 0.5f - style.CellPadding.Y,
            TableVerticalAlignment.Bottom => style.CellPadding.Y,
            _ => 0f
        };
        
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
    /// Draws a header cell with alignment and optional custom color.
    /// </summary>
    /// <param name="label">The header label.</param>
    /// <param name="hAlign">Horizontal alignment.</param>
    /// <param name="vAlign">Vertical alignment.</param>
    /// <param name="sortable">Whether the header should support sorting.</param>
    /// <param name="color">Optional text color.</param>
    public static void DrawAlignedHeaderCell(
        string label,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        bool sortable = false,
        Vector4? color = null)
    {
        var textSize = ImGui.CalcTextSize(label);
        var cellSize = ImGui.GetContentRegionAvail();
        var style = ImGui.GetStyle();
        
        const float sortArrowWidth = 20f;
        var effectiveCellWidth = sortable ? cellSize.X - sortArrowWidth : cellSize.X;
        
        float offsetX = hAlign switch
        {
            TableHorizontalAlignment.Center => (effectiveCellWidth - textSize.X) * 0.5f,
            TableHorizontalAlignment.Right => effectiveCellWidth - textSize.X,
            _ => 0f
        };
        
        float offsetY = vAlign switch
        {
            TableVerticalAlignment.Center => (style.CellPadding.Y * 2 + textSize.Y - textSize.Y) * 0.5f - style.CellPadding.Y,
            TableVerticalAlignment.Bottom => style.CellPadding.Y,
            _ => 0f
        };
        
        if (offsetX > 0f || offsetY != 0f)
        {
            var cursorPos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new Vector2(cursorPos.X + Math.Max(0f, offsetX), cursorPos.Y + offsetY));
        }
        
        if (color.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
        }
        
        ImGui.TableHeader(label);
        
        if (color.HasValue)
        {
            ImGui.PopStyleColor();
        }
    }
    
    /// <summary>
    /// Applies alternating row background color.
    /// </summary>
    /// <param name="rowIndex">The current row index.</param>
    /// <param name="evenRowColor">Color for even rows (optional).</param>
    /// <param name="oddRowColor">Color for odd rows (optional).</param>
    public static void ApplyRowColor(int rowIndex, Vector4? evenRowColor, Vector4? oddRowColor)
    {
        var isEven = rowIndex % 2 == 0;
        if (isEven && evenRowColor.HasValue)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(evenRowColor.Value));
        }
        else if (!isEven && oddRowColor.HasValue)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(oddRowColor.Value));
        }
    }
    
    /// <summary>
    /// Draws a color option with enable/disable toggle for use in settings UI.
    /// </summary>
    /// <param name="label">The label for the color option.</param>
    /// <param name="currentColor">The current color value (null if not set).</param>
    /// <param name="setColor">Callback to set the new color value.</param>
    /// <returns>True if the color was changed.</returns>
    public static bool DrawColorOption(string label, Vector4? currentColor, Action<Vector4?> setColor)
    {
        var changed = false;
        var hasColor = currentColor.HasValue;
        var color = currentColor ?? new Vector4(0.3f, 0.3f, 0.3f, 0.5f);
        
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
        
        ImGui.BeginDisabled(!hasColor);
        if (ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            setColor(color);
            changed = true;
        }
        ImGui.EndDisabled();
        
        return changed;
    }
    
    /// <summary>
    /// Draws alignment combo boxes for horizontal and vertical alignment.
    /// </summary>
    /// <param name="horizontalLabel">Label for horizontal alignment combo.</param>
    /// <param name="verticalLabel">Label for vertical alignment combo.</param>
    /// <param name="hAlign">Current horizontal alignment.</param>
    /// <param name="vAlign">Current vertical alignment.</param>
    /// <param name="setHAlign">Callback to set horizontal alignment.</param>
    /// <param name="setVAlign">Callback to set vertical alignment.</param>
    /// <returns>True if any alignment was changed.</returns>
    public static bool DrawAlignmentCombos(
        string horizontalLabel,
        string verticalLabel,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        Action<TableHorizontalAlignment> setHAlign,
        Action<TableVerticalAlignment> setVAlign)
    {
        var changed = false;
        
        var hAlignInt = (int)hAlign;
        if (ImGui.Combo(horizontalLabel, ref hAlignInt, "Left\0Center\0Right\0"))
        {
            setHAlign((TableHorizontalAlignment)hAlignInt);
            changed = true;
        }
        
        var vAlignInt = (int)vAlign;
        if (ImGui.Combo(verticalLabel, ref vAlignInt, "Top\0Center\0Bottom\0"))
        {
            setVAlign((TableVerticalAlignment)vAlignInt);
            changed = true;
        }
        
        return changed;
    }
    
    /// <summary>
    /// Formats a number with optional compact notation.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="compact">Whether to use compact notation.</param>
    /// <returns>Formatted string.</returns>
    public static string FormatNumber(long value, bool compact = false)
    {
        if (!compact)
        {
            return value.ToString("N0");
        }
        
        return FormatUtils.FormatAbbreviated(value);
    }
}
