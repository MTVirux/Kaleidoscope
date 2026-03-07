using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Table;

/// <summary>
/// A generic, reusable table widget with customizable columns, sorting, styling,
/// and a built-in header right-click context menu for column resizing.
/// Cell content rendering is delegated to the caller via delegates.
/// Delegates shared table plumbing (resize, selection, width capture, etc.) to <see cref="TableCore"/>.
/// </summary>
/// <typeparam name="TRow">The type of data for each row.</typeparam>
public class TableWidget<TRow>
{
    /// <summary>
    /// Shared default settings instance to avoid allocating per frame when no settings are bound.
    /// </summary>
    private static readonly MTTableSettings DefaultSettings = new();
    
    private readonly string _tableId;
    private readonly string _noDataText;
    
    /// <summary>
    /// Shared table infrastructure for resize, selection, header rendering, etc.
    /// </summary>
    private readonly TableCore _core;
    
    // Settings binding
    private ITableSettings? _boundSettings;
    private Action? _onSettingsChanged;
    private string _settingsName = "Table Settings";
    
    // Optional content width measurer for "resize to data width"
    private Func<TRow, int, float>? _contentWidthMeasurer;
    
    // Cached sort results to avoid per-frame ToList() allocation
    private List<TRow>? _cachedSortedRows;
    private IReadOnlyList<TRow>? _lastInputRows;
    private bool _sortDirty = true;
    
    /// <summary>
    /// Raised when column merge groups change (merge or unmerge action).
    /// Subscribers can use this to refresh display column lists.
    /// </summary>
    public event Action? OnMergeChanged;
    
    /// <summary>
    /// Gets whether this widget has bound settings.
    /// </summary>
    public bool HasSettings => _boundSettings != null;
    
    /// <summary>
    /// Gets the display name for settings.
    /// </summary>
    public string SettingsName => _settingsName;
    
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
    /// Creates a new TableWidget.
    /// </summary>
    /// <param name="tableId">Unique ID for ImGui table identification.</param>
    /// <param name="noDataText">Text to display when there is no data.</param>
    public TableWidget(string tableId, string noDataText = "No data available.")
    {
        _tableId = tableId;
        _noDataText = noDataText;
        _core = new TableCore(tableId);
        _core.OnMergeChanged += () => OnMergeChanged?.Invoke();
    }
    
    /// <summary>
    /// Binds this widget to a settings object for automatic synchronization.
    /// </summary>
    /// <param name="settings">The settings object implementing ITableSettings.</param>
    /// <param name="onSettingsChanged">Callback when settings are changed (e.g., to trigger config save).</param>
    /// <param name="settingsName">Display name for the settings section.</param>
    public void BindSettings(
        ITableSettings settings,
        Action? onSettingsChanged = null,
        string settingsName = "Table Settings")
    {
        _boundSettings = settings;
        _onSettingsChanged = onSettingsChanged;
        _settingsName = settingsName;
    }
    
    /// <summary>
    /// Sets a delegate that measures the rendered content width for a given row and column.
    /// This enables the "Resize to data width" context menu option.
    /// The delegate should return the pixel width of the cell content (text, icons, etc.).
    /// If not set, the "Resize to data width" options will not appear.
    /// </summary>
    /// <param name="measurer">Function taking (row, columnIndex) and returning content width in pixels.</param>
    /// <returns>This widget for fluent chaining.</returns>
    public TableWidget<TRow> WithContentWidthMeasurer(Func<TRow, int, float> measurer)
    {
        _contentWidthMeasurer = measurer;
        return this;
    }
    
    /// <summary>
    /// Marks a column as non-resizable. Non-resizable columns won't show the resize context menu
    /// on right-click and their width is excluded from fill calculations. Use this for fixed-width
    /// label columns (e.g., a "Character" column) that should not participate in resize operations.
    /// </summary>
    /// <param name="columnIndex">The 0-based column index to mark as non-resizable.</param>
    /// <returns>This widget for fluent chaining.</returns>
    public TableWidget<TRow> WithNoResizeColumn(int columnIndex)
    {
        _core.AddNoResizeColumn(columnIndex);
        return this;
    }
    
    /// <summary>
    /// Forces a table state reset, causing ImGui to re-read column init widths.
    /// Call this after programmatically changing column widths outside of the context menu.
    /// </summary>
    public void ResetColumnWidthState()
    {
        _core.ResetColumnWidthState();
    }
    
    /// <summary>
    /// Draws the table with built-in header context menu for column resizing.
    /// </summary>
    /// <param name="columns">Column definitions. Widths will be updated when user resizes.</param>
    /// <param name="rows">Row data.</param>
    /// <param name="cellRenderer">Delegate to render each cell's content.</param>
    /// <param name="sortKeySelector">Optional delegate to get sort keys. If null, sorting uses row order.</param>
    /// <param name="settings">Optional settings override. If null, uses bound settings.</param>
    /// <param name="height">Optional explicit height. If 0, uses available height.</param>
    public void Draw(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<TRow> rows,
        CellRenderer cellRenderer,
        SortKeySelector? sortKeySelector = null,
        ITableSettings? settings = null,
        float height = 0f)
    {
        settings ??= _boundSettings ?? DefaultSettings;
        
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
        
        // Handle pending column resize actions from the context menu (deferred to next frame)
        _core.HandlePendingResize(columns, rows, _contentWidthMeasurer, _onSettingsChanged);
        
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti | ImGuiTableFlags.SortTristate;
        
        var tableHeight = height > 0 ? height : ImGui.GetContentRegionAvail().Y;
        
        // Use core's effective table ID (includes suffix for forced resets)
        var tableId = _core.GetEffectiveTableId();
        if (!ImGui.BeginTable(tableId, columns.Count, flags, new Vector2(0, tableHeight)))
            return;
        
        try
        {
            // Setup columns via core
            _core.SetupColumns(columns, settings.SortColumns, settings.SortColumnIndex, settings.SortAscending);
            
            if (settings.FreezeHeader)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
            }
            
            // Apply header color if set
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, settings.HeaderColor.Value);
            }
            
            // Handle selection clearing
            var isShiftHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
            var isPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
            _core.HandleSelectionClearing(isShiftHeld, isPopupOpen);
            
            // Draw header row via core
            _core.DrawHeaderRow(
                columns,
                settings.HeaderHorizontalAlignment,
                settings.HeaderVerticalAlignment,
                settings.Sortable,
                isShiftHeld,
                isPopupOpen,
                settings.MergedColumnGroups,
                _contentWidthMeasurer != null,
                _onSettingsChanged);
            
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
                    
                    // Highlight selected columns in data cells too
                    if (_core.SelectedColumnIndices.Contains(colIdx))
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.8f, 0.4f)));
                    }
                    
                    var context = new CellRenderContext
                    {
                        RowIndex = rowIdx,
                        ColumnIndex = colIdx,
                        Settings = settings
                    };
                    
                    cellRenderer(row, context);
                }
            }
            
            // Capture column widths via core
            _core.CaptureColumnWidths(columns, _onSettingsChanged);
        }
        finally
        {
            ImGui.EndTable();
        }
    }
    
    #region Sorting
    
    private List<TRow> GetSortedRows(
        IReadOnlyList<TRow> rows,
        SortKeySelector? sortKeySelector,
        ITableSettings settings)
    {
        // Detect if input data reference changed
        if (!ReferenceEquals(rows, _lastInputRows))
        {
            _lastInputRows = rows;
            _sortDirty = true;
        }
        
        if (!settings.Sortable || sortKeySelector == null)
        {
            if (_sortDirty || _cachedSortedRows == null)
            {
                _cachedSortedRows = rows.ToList();
                _sortDirty = false;
            }
            return _cachedSortedRows;
        }
        
        // Read sort specs via core
        var sc = settings.SortColumns;
        var sci = settings.SortColumnIndex;
        var sa = settings.SortAscending;
        var sortColumns = _core.ReadSortSpecs(
            settings.Sortable,
            ref sc,
            ref sci,
            ref sa,
            () =>
            {
                _sortDirty = true;
                _onSettingsChanged?.Invoke();
            });
        settings.SortColumns = sc;
        settings.SortColumnIndex = sci;
        settings.SortAscending = sa;
        
        // Return cached result if nothing changed
        if (!_sortDirty && _cachedSortedRows != null)
            return _cachedSortedRows;
        
        // Sort the rows using multi-column sort (left-to-right priority)
        var sorted = rows.ToList();
        sorted.Sort((a, b) =>
        {
            foreach (var sortCol in sortColumns)
            {
                var keyA = sortKeySelector(a, sortCol.ColumnIndex);
                var keyB = sortKeySelector(b, sortCol.ColumnIndex);
                
                int result;
                if (keyA == null && keyB == null) { result = 0; }
                else if (keyA == null) { result = sortCol.Ascending ? -1 : 1; }
                else if (keyB == null) { result = sortCol.Ascending ? 1 : -1; }
                else { result = sortCol.Ascending ? keyA.CompareTo(keyB) : -keyA.CompareTo(keyB); }
                
                if (result != 0) return result;
            }
            return 0;
        });
        
        _cachedSortedRows = sorted;
        _sortDirty = false;
        return sorted;
    }
    
    #endregion
    
    #region Settings UI
    
    /// <summary>
    /// Draws the settings UI for this table widget.
    /// </summary>
    /// <returns>True if any setting was changed.</returns>
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
        if (TreeHelpers.DrawSection("Data Column Alignment", true))
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
            TreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (TreeHelpers.DrawSection("Header Row Alignment"))
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
            TreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (TreeHelpers.DrawSection("Row Colors"))
        {
            // Header color
            changed |= TableHelpers.DrawColorOption("Header", settings.HeaderColor, c => settings.HeaderColor = c);
        
            // Even row color
            changed |= TableHelpers.DrawColorOption("Even Rows", settings.EvenRowColor, c => settings.EvenRowColor = c);
        
            // Odd row color
            changed |= TableHelpers.DrawColorOption("Odd Rows", settings.OddRowColor, c => settings.OddRowColor = c);
            TreeHelpers.EndSection();
        }
        
        if (changed)
        {
            _onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    #endregion
    
    #region Static Helpers
    
    /// <summary>
    /// Handles SHIFT+click/drag range selection for column or row headers.
    /// Returns the updated selection state for the current index.
    /// Can be called by external table implementations that need the same selection behavior.
    /// Delegates to <see cref="TableCore.HandleShiftSelection"/>.
    /// </summary>
    public static bool HandleShiftSelection(
        int currentIdx,
        HashSet<int> selectedIndices,
        ref bool isSelecting,
        ref int selectionStart)
        => TableCore.HandleShiftSelection(currentIdx, selectedIndices, ref isSelecting, ref selectionStart);
    
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
    public static void DrawAlignedText(string text, ITableSettings settings, Vector4? color = null)
    {
        TableHelpers.DrawAlignedCellText(text, settings.DataHorizontalAlignment, settings.DataVerticalAlignment, color);
    }
    
    #endregion
}
