using Kaleidoscope.Gui.Widgets.Common;

namespace Kaleidoscope.Gui.Widgets.Table;

/// <summary>
/// Represents a single sort column entry for multi-column sorting.
/// </summary>
public sealed class SortColumnEntry
{
    /// <summary>
    /// The column index to sort by.
    /// </summary>
    public int ColumnIndex { get; set; }

    /// <summary>
    /// Whether this column sorts in ascending order.
    /// </summary>
    public bool Ascending { get; set; }
}

/// <summary>
/// Settings interface for the generic table widget.
/// Implement this interface in your settings class to enable automatic settings binding.
/// </summary>
public interface ITableSettings
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
    /// Ordered list of sort columns for multi-column sorting.
    /// Priority is left-to-right (first entry is the primary sort).
    /// </summary>
    List<SortColumnEntry> SortColumns { get; set; }
    
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
    
    /// <summary>
    /// Number format configuration for numeric values in the table.
    /// </summary>
    NumberFormatConfig NumberFormat { get; set; }
    
    /// <summary>
    /// List of merged column groups. Each group combines multiple columns into a single display column.
    /// When columns are merged, they appear as one column and callers can aggregate values across them.
    /// </summary>
    List<MergedColumnGroupBase> MergedColumnGroups { get; set; }
}

/// <summary>
/// Default implementation of ITableSettings.
/// </summary>
public class MTTableSettings : ITableSettings
{
    /// <inheritdoc/>
    public bool Sortable { get; set; } = true;
    
    /// <inheritdoc/>
    public int SortColumnIndex { get; set; } = 0;
    
    /// <inheritdoc/>
    public bool SortAscending { get; set; } = true;

    /// <inheritdoc/>
    public List<SortColumnEntry> SortColumns { get; set; } = new();
    
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
    
    /// <inheritdoc/>
    public NumberFormatConfig NumberFormat { get; set; } = new();
    
    /// <inheritdoc/>
    public List<MergedColumnGroupBase> MergedColumnGroups { get; set; } = new();
}

/// <summary>
/// Column definition for a generic table.
/// </summary>
public class TableColumn
{
    /// <summary>
    /// Column header text.
    /// </summary>
    public required string Header { get; init; }
    
    /// <summary>
    /// Column width. If 0, uses auto-width (stretch).
    /// </summary>
    public float Width { get; set; } = 0f;
    
    /// <summary>
    /// Whether this column should stretch to fill available space.
    /// </summary>
    public bool Stretch { get; init; } = false;
    
    /// <summary>
    /// ImGui column flags. Defaults to WidthFixed unless Stretch is true.
    /// </summary>
    public Dalamud.Bindings.ImGui.ImGuiTableColumnFlags Flags { get; init; } = Dalamud.Bindings.ImGui.ImGuiTableColumnFlags.None;
    
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
public struct CellRenderContext
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
    public readonly bool IsEvenRow => RowIndex % 2 == 0;
    
    /// <summary>
    /// The table settings.
    /// </summary>
    public required ITableSettings Settings { get; init; }
}

/// <summary>
/// Generic base class for merged column groups.
/// Represents a group of columns that display aggregated/summed values as a single column.
/// </summary>
public class MergedColumnGroupBase
{
    /// <summary>
    /// Custom display name for the merged column header.
    /// </summary>
    public string Name { get; set; } = "Merged";
    
    /// <summary>
    /// List of column indices (0-based) that are merged into this group.
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
/// Generic base class for merged row groups.
/// Represents a group of rows that display aggregated/summed values as a single row.
/// </summary>
/// <typeparam name="TKey">The type of key used to identify rows (e.g., string, int, ulong).</typeparam>
public class MergedRowGroupBase<TKey>
{
    /// <summary>
    /// Custom display name for the merged row.
    /// </summary>
    public string Name { get; set; } = "Merged";
    
    /// <summary>
    /// List of row keys that are merged into this group.
    /// </summary>
    public List<TKey> RowKeys { get; set; } = new();
    
    /// <summary>
    /// Optional custom color for the merged row. If null, uses default.
    /// </summary>
    public Vector4? Color { get; set; }
}
